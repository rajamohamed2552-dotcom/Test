# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/GKM.AssetManagement/GKM.AssetManagement.csproj src/GKM.AssetManagement/
RUN dotnet restore src/GKM.AssetManagement/GKM.AssetManagement.csproj
COPY src/ src/
RUN dotnet publish src/GKM.AssetManagement/GKM.AssetManagement.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_ENVIRONMENT=Production \
    DisableHttpsRedirection=true \
    PORT=8080
EXPOSE 8080
RUN mkdir /keys && chown $APP_UID /keys
USER $APP_UID
ENTRYPOINT ["dotnet", "GKM.AssetManagement.dll"]

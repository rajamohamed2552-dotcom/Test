using GKM.AssetManagement.Data;
using GKM.AssetManagement.Models;
using GKM.AssetManagement.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Machine-specific secrets (e.g. a real connection string) live in a git-ignored file or in environment variables.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

// Cloud hosts (Render, Heroku-style) hand us the port via PORT.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port)) builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Database: PostgreSQL by default; "Sqlite" is available for quick local runs without a Postgres server.
var provider = builder.Configuration["Database:Provider"] ?? "Postgres";
builder.Services.AddDbContext<AppDbContext>(o =>
{
    if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
        o.UseSqlite(builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=gkm-assets.db");
    else
    {
        // Dates are stored as plain "timestamp without time zone" (business dates, not instants).
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        o.UseNpgsql(ToNpgsql(builder.Configuration.GetConnectionString("Postgres") ?? builder.Configuration["DATABASE_URL"]));
    }
});

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o =>
    {
        o.Password.RequiredLength = 8;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.AccessDeniedPath = "/Account/AccessDenied";
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;
});

// Everything requires a signed-in user unless explicitly marked [AllowAnonymous].
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddScoped<AssetService>();
builder.Services.AddScoped<DepreciationService>();
builder.Services.AddControllersWithViews();
builder.Services.AddHealthChecks();

// Persist cookie/antiforgery keys outside the container filesystem so sign-ins survive restarts and redeploys.
var keysPath = builder.Configuration["DataProtection:KeysPath"] ?? (Directory.Exists("/keys") ? "/keys" : null);
if (keysPath != null) builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath)).SetApplicationName("GKM.AssetManagement");

// Behind a reverse proxy the platform terminates TLS; trust its X-Forwarded-* headers so redirects and cookies use https.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// In a container the proxy already handles HTTPS; only redirect when the app itself serves both schemes.
if (!app.Configuration.GetValue("DisableHttpsRedirection", false)) app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/healthz").AllowAnonymous();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");

// NOTE: EnsureCreated suits the MVP. For production, switch to EF Core migrations (dotnet ef migrations add Initial).
using (var scope = app.Services.CreateScope())
    await SeedData.InitializeAsync(scope.ServiceProvider, builder.Configuration.GetValue("Seed:SampleData", true));

app.Run();

// Hosts such as Railway, Render and Neon hand out postgresql://user:pass@host:port/db URLs; Npgsql wants key=value pairs.
static string? ToNpgsql(string? cs)
{
    if (cs is null || !(cs.StartsWith("postgres://") || cs.StartsWith("postgresql://"))) return cs;
    var uri = new Uri(cs);
    var user = uri.UserInfo.Split(':', 2);
    var host = uri.Host;
    var ssl = host.EndsWith(".railway.internal") ? "Disable" : "Prefer"; // private network needs no TLS; public proxies use it
    return $"Host={host};Port={(uri.Port > 0 ? uri.Port : 5432)};Database={uri.AbsolutePath.TrimStart('/')};Username={Uri.UnescapeDataString(user[0])};Password={Uri.UnescapeDataString(user.ElementAtOrDefault(1) ?? "")};SSL Mode={ssl};Trust Server Certificate=true";
}

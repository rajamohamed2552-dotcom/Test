# GKM Asset Management

Enterprise asset management and tracking for GKM, owned by Finance & Cost Management.
ASP.NET Core MVC (.NET 10) · EF Core · PostgreSQL · ASP.NET Identity (cookie auth, role-based).

## Run it

**With PostgreSQL (default)**
```
docker compose up -d                      # or point ConnectionStrings:Postgres at your own server
dotnet run --project src/GKM.AssetManagement
```

**Without Postgres (quick local trial, uses a SQLite file)**
```
$env:Database__Provider = "Sqlite"        # PowerShell;  bash: export Database__Provider=Sqlite
dotnet run --project src/GKM.AssetManagement
```

On first start the schema is created and seeded (3 plants, cost centres, GL accounts, 10 asset categories,
~40 sample assets with six months of posted depreciation, work orders, movements and an in-progress audit).
Set `Seed:SampleData` to `false` to seed only roles, users and master data.

Tests: `dotnet test`

## Demo users (password `Gkm@12345`, development only)

| User | Role | Can do |
|---|---|---|
| admin@gkm.com | Admin | Everything |
| finance.manager@gkm.com | FinanceManager | Approve onboarding, run depreciation, dispose, master data, classification |
| accountant@gkm.com | AssetAccountant | Register/edit assets, transfers, reports |
| plant.manager@gkm.com | PlantManager | Register assets, transfers, raise work orders |
| maintenance@gkm.com | MaintenanceEngineer | Work orders and PM plans |
| auditor@gkm.com | Auditor | Audit campaigns, verification, reports, activity trail |

## What is covered

| Process | Where | Highlights |
|---|---|---|
| Classification | Classification | Category = default depreciation method/life/salvage, capitalisation threshold, GL mapping |
| Onboarding | Assets → Onboarding approvals | Draft → Pending → Active/Rejected; maker-checker (requester cannot approve); auto tag `GKM-BLR-MCH-00042`; below-threshold items blocked |
| Maintenance | Maintenance | Work orders (preventive/corrective/inspection/calibration), PM plans with due-date generation, cost capture (labour/parts/external), asset status follows work |
| Tracking | Tracking & transfers | Plant/location/cost-centre/custodian transfers with history; location headcount view |
| Audit | Audits, Activity trail | Campaign snapshots in-scope assets; Verified/Missing/Wrong location/Condition findings; close only when complete; CSV export; immutable per-asset event log |
| Finance | Depreciation, Reports, Master data | Straight-line and double-declining, never below salvage, one run per period; GL journal per cost centre; cost-centre summary; TCO; disposal gain/loss; CSV exports |

## Deploy for free (GitHub + open-source/free-tier hosting)

GitHub stores the code and runs CI; it cannot host an ASP.NET server (GitHub Pages is static-only), so the app runs on a host that pulls from your GitHub repo.

**Option A: Render (free web service) + Neon (free PostgreSQL)**
1. Push this folder to a new GitHub repo (`.gitignore` already excludes `appsettings.Local.json` and `.env`).
2. [neon.tech](https://neon.tech): create a free project and copy the connection details. Use them as an Npgsql string:
   `Host=<host>;Database=<db>;Username=<user>;Password=<pw>;SSL Mode=Require;Trust Server Certificate=true`
3. [render.com](https://render.com): *New > Blueprint*, select the repo (uses `render.yaml` and the `Dockerfile`). When prompted, set:
   - `ConnectionStrings__Postgres` = the Neon string
   - `Seed__DemoPassword` = a strong password (used for the built-in demo users)
4. Open the Render URL and sign in as `admin@gkm.com` with that password.

Free-tier caveats: Render sleeps the app after ~15 min idle (first request takes ~30-60 s to wake) and cookie keys reset on each deploy because the free tier has no disk, so users must sign in again after a redeploy. Neon free is 0.5 GB.

**Option C: Railway (app + PostgreSQL in one project)**

Railway has a $5 trial credit, then the Hobby plan (about $5/month usage-based); it is not permanently free.
1. Push the repo to GitHub.
2. [railway.app](https://railway.app): *New Project > Deploy from GitHub repo* and pick the repo. It builds the `Dockerfile` and reads `railway.json` (health check `/healthz`).
3. In the same project: *+ New > Database > PostgreSQL*.
4. Open the app service > *Variables* and add:
   - `ConnectionStrings__Postgres` = `${{Postgres.DATABASE_URL}}` (the app converts the `postgresql://` URL itself)
   - `Seed__AdminEmail` = your admin email, `Seed__AdminPassword` = a strong password
   - `Seed__SampleData` = `false` for real use, or `true` for demo data (then also set `Seed__DemoPassword`)
5. App service > *Settings > Networking > Generate Domain*, then open the URL and sign in.
6. Optional: attach a Volume mounted at `/keys` so sign-ins survive redeploys.

**Option B: your own server or VM (all open source: app + PostgreSQL + Caddy with automatic HTTPS)**
```
cp .env.example .env        # set DOMAIN, DB_PASSWORD, ADMIN_EMAIL, ADMIN_PASSWORD
docker compose -f docker-compose.prod.yml up -d --build
```
Creates one admin account from `ADMIN_EMAIL`/`ADMIN_PASSWORD` (no demo users unless `SEED_SAMPLE_DATA=true`). Keys persist in a volume.

**CI:** `.github/workflows/ci.yml` builds, tests and builds the Docker image on every push and pull request.

**Configuration reference (environment variables)**

| Variable | Purpose |
|---|---|
| `ConnectionStrings__Postgres` | Database connection (never commit a real one; locally use `appsettings.Local.json`) |
| `Seed__AdminEmail`, `Seed__AdminPassword` | First-run admin account (required outside Development) |
| `Seed__SampleData` | `true` loads demo users and ~40 sample assets; `false` for real use |
| `Seed__DemoPassword` | Password for the demo users when sample data is on (defaults to the admin password) |
| `PORT`, `DisableHttpsRedirection` | Set by the Dockerfile for proxy-terminated HTTPS |
| `DataProtection__KeysPath` | Folder for cookie keys (defaults to `/keys` when it exists) |

## Layout

```
src/GKM.AssetManagement/
  Models/Entities.cs         domain model and enums
  Data/                      DbContext, seed data
  Services/                  DepreciationService/Calculator, AssetService (tags, event log)
  Controllers/ Views/        one controller + views per process
tests/GKM.AssetManagement.Tests/   depreciation maths and run rules
```

## Before going to production

- Replace `EnsureCreated` with EF Core migrations (`dotnet ef migrations add Initial`) and run them in your pipeline.
- Remove the seeded demo users or change their passwords; move the DB password to a secret store.
- Integrate with corporate SSO (Entra ID / OIDC) instead of local Identity accounts.
- Plant-level data scoping (e.g. Plant Managers see only their plant) is not enforced yet; `ApplicationUser.PlantId` is in place for it.
- The depreciation run produces the journal as a CSV; posting it to SAP/ERP needs an integration.
- Dashboard aggregation is done in memory, which is fine for tens of thousands of assets; push to SQL for larger registers.
- Dates are stored as business dates (no time zone). Number formatting follows the server culture.

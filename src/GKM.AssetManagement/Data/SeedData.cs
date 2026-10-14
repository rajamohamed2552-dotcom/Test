using GKM.AssetManagement.Models;
using GKM.AssetManagement.Services;
using System.Data.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace GKM.AssetManagement.Data;

public static class SeedData
{
    // Fallback for local development only. Outside Development a password must be supplied via Seed:DemoPassword.
    private const string DevDemoPassword = "Gkm@12345";

    public static async Task InitializeAsync(IServiceProvider sp, bool sampleData)
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var env = sp.GetRequiredService<IHostEnvironment>();
        // Sample mode seeds six demo users sharing one password; otherwise only a single real admin account is created.
        var adminPassword = config["Seed:AdminPassword"] ?? (env.IsDevelopment() ? DevDemoPassword : null);
        var adminEmail = config["Seed:AdminEmail"] ?? (env.IsDevelopment() ? "admin@gkm.com" : null);
        var demoPassword = config["Seed:DemoPassword"] ?? adminPassword;
        var db = sp.GetRequiredService<AppDbContext>();
        await EnsureSchemaAsync(db);

        var roleMgr = sp.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var r in Roles.All)
            if (!await roleMgr.RoleExistsAsync(r)) await roleMgr.CreateAsync(new IdentityRole(r));

        if (await db.Plants.AnyAsync()) return; // already seeded

        if (sampleData ? demoPassword is null : adminPassword is null || adminEmail is null)
            throw new InvalidOperationException("First-run seeding needs credentials. Set Seed__AdminEmail and Seed__AdminPassword (and Seed__DemoPassword if Seed__SampleData=true). Password: min 8 chars with upper, lower, digit and symbol.");

        var plants = new[]
        {
            new Plant { Code = "BLR", Name = "Bengaluru Vehicle Plant", City = "Bengaluru" },
            new Plant { Code = "PUN", Name = "Pune Powertrain Plant", City = "Pune" },
            new Plant { Code = "CHN", Name = "Chennai Assembly Plant", City = "Chennai" },
        };
        db.Plants.AddRange(plants);
        await db.SaveChangesAsync();

        string[] areas = { "Press Shop", "Body Shop", "Paint Shop", "Assembly Line", "Machine Shop", "Tool Room", "Warehouse", "IT Server Room", "Admin Block" };
        foreach (var p in plants)
            foreach (var a in areas) db.Locations.Add(new Location { PlantId = p.Id, Name = a });

        foreach (var p in plants)
        {
            db.CostCenters.Add(new CostCenter { Code = $"{p.Code}-PRD", Name = $"{p.City} Production", PlantId = p.Id, Owner = "Plant Head" });
            db.CostCenters.Add(new CostCenter { Code = $"{p.Code}-MNT", Name = $"{p.City} Maintenance", PlantId = p.Id, Owner = "Maintenance Head" });
            db.CostCenters.Add(new CostCenter { Code = $"{p.Code}-QLT", Name = $"{p.City} Quality", PlantId = p.Id, Owner = "Quality Head" });
        }
        db.CostCenters.Add(new CostCenter { Code = "CORP-IT", Name = "Corporate IT", Owner = "CIO" });
        db.CostCenters.Add(new CostCenter { Code = "CORP-ADM", Name = "Corporate Administration", Owner = "Head of Admin" });

        var gl = new (string Code, string Name, GlAccountType Type)[]
        {
            ("150100", "Plant & Machinery", GlAccountType.Asset), ("150200", "Tooling, Dies & Jigs", GlAccountType.Asset),
            ("150300", "Vehicles & Material Handling", GlAccountType.Asset), ("150400", "IT Equipment", GlAccountType.Asset),
            ("150500", "Buildings & Civil", GlAccountType.Asset), ("150600", "Furniture & Fixtures", GlAccountType.Asset),
            ("150700", "Land", GlAccountType.Asset),
            ("160100", "Accum. Depreciation - Plant & Machinery", GlAccountType.AccumulatedDepreciation),
            ("160200", "Accum. Depreciation - Tooling", GlAccountType.AccumulatedDepreciation),
            ("160300", "Accum. Depreciation - Vehicles", GlAccountType.AccumulatedDepreciation),
            ("160400", "Accum. Depreciation - IT", GlAccountType.AccumulatedDepreciation),
            ("160500", "Accum. Depreciation - Buildings", GlAccountType.AccumulatedDepreciation),
            ("160600", "Accum. Depreciation - Furniture", GlAccountType.AccumulatedDepreciation),
            ("610100", "Depreciation Expense - Plant & Machinery", GlAccountType.DepreciationExpense),
            ("610200", "Depreciation Expense - Tooling", GlAccountType.DepreciationExpense),
            ("610300", "Depreciation Expense - Vehicles", GlAccountType.DepreciationExpense),
            ("610400", "Depreciation Expense - IT", GlAccountType.DepreciationExpense),
            ("610500", "Depreciation Expense - Buildings", GlAccountType.DepreciationExpense),
            ("610600", "Depreciation Expense - Furniture", GlAccountType.DepreciationExpense),
            ("620100", "Repairs & Maintenance Expense", GlAccountType.MaintenanceExpense),
            ("690100", "Gain/Loss on Asset Disposal", GlAccountType.GainLossOnDisposal),
        };
        db.GlAccounts.AddRange(gl.Select(g => new GlAccount { Code = g.Code, Name = g.Name, Type = g.Type }));
        await db.SaveChangesAsync();
        var gls = await db.GlAccounts.ToDictionaryAsync(g => g.Code);

        // code, name, class, method, life(months), salvage%, glIndex (1..6), threshold
        var cats = new (string Code, string Name, string Class, DepreciationMethod M, int Life, decimal Salv, int Gl, decimal Thr)[]
        {
            ("MCH", "Press & Stamping Machines", "Plant & Machinery", DepreciationMethod.StraightLine, 180, 5, 1, 50000),
            ("ROB", "Welding & Assembly Robots", "Plant & Machinery", DepreciationMethod.StraightLine, 120, 5, 1, 50000),
            ("CNC", "CNC & Machine Tools", "Plant & Machinery", DepreciationMethod.DecliningBalance, 120, 5, 1, 50000),
            ("TLG", "Tooling, Dies & Jigs", "Tooling", DepreciationMethod.DecliningBalance, 60, 0, 2, 25000),
            ("TST", "Testing & Quality Equipment", "Plant & Machinery", DepreciationMethod.StraightLine, 96, 5, 1, 25000),
            ("VEH", "Vehicles & Material Handling", "Vehicles", DepreciationMethod.StraightLine, 96, 10, 3, 25000),
            ("ITE", "IT Equipment", "IT", DepreciationMethod.StraightLine, 48, 0, 4, 10000),
            ("BLD", "Buildings & Civil Works", "Buildings", DepreciationMethod.StraightLine, 480, 10, 5, 100000),
            ("FUR", "Furniture & Fixtures", "Furniture", DepreciationMethod.StraightLine, 120, 0, 6, 5000),
            ("LND", "Land", "Land", DepreciationMethod.NoDepreciation, 1, 0, 7, 0),
        };
        foreach (var c in cats)
        {
            var n = c.Gl;
            db.AssetCategories.Add(new AssetCategory
            {
                Code = c.Code, Name = c.Name, AssetClass = c.Class, DefaultMethod = c.M, DefaultUsefulLifeMonths = c.Life,
                DefaultSalvagePercent = c.Salv, CapitalizationThreshold = c.Thr,
                AssetGlAccountId = gls[GlCode("15", n)].Id,
                AccumDepGlAccountId = n <= 6 ? gls[GlCode("16", n)].Id : null,
                DepExpenseGlAccountId = n <= 6 ? gls[GlCode("61", n)].Id : null,
            });
        }
        await db.SaveChangesAsync();

        (string Email, string Name, string Role, string? Plant, string Password)[] users = sampleData
            ? new (string Email, string Name, string Role, string? Plant, string Password)[]
            {
                (adminEmail ?? "admin@gkm.com", "System Administrator", Roles.Admin, null, adminPassword ?? demoPassword!),
                ("finance.manager@gkm.com", "Meera Iyer (Finance Manager)", Roles.FinanceManager, null, demoPassword!),
                ("accountant@gkm.com", "Arun Kumar (Asset Accountant)", Roles.AssetAccountant, null, demoPassword!),
                ("plant.manager@gkm.com", "Suresh Rao (Plant Manager)", Roles.PlantManager, "BLR", demoPassword!),
                ("maintenance@gkm.com", "Karthik N (Maintenance Engineer)", Roles.MaintenanceEngineer, "BLR", demoPassword!),
                ("auditor@gkm.com", "Priya Nair (Internal Auditor)", Roles.Auditor, null, demoPassword!),
            }
            : new[] { (adminEmail!, "System Administrator", Roles.Admin, (string?)null, adminPassword!) };
        var userMgr = sp.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var u in users)
        {
            var user = new ApplicationUser
            {
                UserName = u.Email, Email = u.Email, EmailConfirmed = true, FullName = u.Name,
                PlantId = u.Plant == null ? null : plants.First(p => p.Code == u.Plant).Id
            };
            var res = await userMgr.CreateAsync(user, u.Password);
            if (!res.Succeeded)
                throw new InvalidOperationException($"Could not create user {u.Email}: {string.Join("; ", res.Errors.Select(e => e.Description))}");
            await userMgr.AddToRoleAsync(user, u.Role);
        }

        if (sampleData) await SeedSampleAssetsAsync(sp, db);
    }

    /// <summary>
    /// EnsureCreated is a no-op when the database already holds any tables (e.g. a shared database),
    /// so create our tables explicitly if they are missing.
    /// </summary>
    private static async Task EnsureSchemaAsync(AppDbContext db)
    {
        var creator = db.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync()) await creator.CreateAsync();
        // Probe for our own tables with a separate, log-free context so the expected failure isn't reported as an error.
        var cs = db.Database.GetConnectionString();
        var probeOptions = (db.Database.IsNpgsql()
            ? new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs)
            : new DbContextOptionsBuilder<AppDbContext>().UseSqlite(cs)).Options;
        try
        {
            await using var probe = new AppDbContext(probeOptions);
            await probe.Roles.AnyAsync();
        }
        catch (DbException)
        {
            await creator.CreateTablesAsync();
        }
    }

    private static string GlCode(string prefix, int n) => $"{prefix}0{n}00";

    private static async Task SeedSampleAssetsAsync(IServiceProvider sp, AppDbContext db)
    {
        var rnd = new Random(42);
        var plants = await db.Plants.Include(p => p.Locations).Include(p => p.CostCenters).ToListAsync();
        var cats = await db.AssetCategories.ToListAsync();
        var corpIt = await db.CostCenters.FirstAsync(c => c.Code == "CORP-IT");
        var assetSvc = new AssetService(db);

        // category code -> (names, cost range, area names)
        var catalog = new Dictionary<string, (string[] Names, int Min, int Max, string[] Areas, string[] Makes)>
        {
            ["MCH"] = (new[] { "Servo Press 800T", "Hydraulic Press 1200T", "Blanking Line", "Transfer Press 2000T" }, 40_000_000, 180_000_000, new[] { "Press Shop" }, new[] { "Schuler", "AIDA", "Komatsu" }),
            ["ROB"] = (new[] { "Spot Welding Robot", "Seam Sealing Robot", "Paint Robot", "Material Handling Robot" }, 3_000_000, 12_000_000, new[] { "Body Shop", "Paint Shop", "Assembly Line" }, new[] { "FANUC", "ABB", "KUKA", "Yaskawa" }),
            ["CNC"] = (new[] { "5-Axis Machining Centre", "CNC Lathe", "Crankshaft Grinder", "Horizontal Machining Centre" }, 8_000_000, 45_000_000, new[] { "Machine Shop" }, new[] { "DMG Mori", "Mazak", "Okuma", "Makino" }),
            ["TLG"] = (new[] { "Door Panel Die Set", "Fender Die Set", "Welding Fixture", "Checking Fixture", "Roof Die Set" }, 1_500_000, 14_000_000, new[] { "Tool Room", "Press Shop", "Body Shop" }, new[] { "In-house", "Toyo Tooling" }),
            ["TST"] = (new[] { "CMM Machine", "Emission Test Bench", "Torque Calibrator", "Chassis Dynamometer" }, 2_000_000, 25_000_000, new[] { "Assembly Line", "Machine Shop" }, new[] { "Zeiss", "AVL", "Horiba" }),
            ["VEH"] = (new[] { "Forklift 5T", "Electric Tow Tractor", "Reach Truck", "Staff Shuttle Bus" }, 1_200_000, 4_500_000, new[] { "Warehouse", "Assembly Line" }, new[] { "Toyota MH", "Godrej", "Tata Motors" }),
            ["ITE"] = (new[] { "Core Network Switch", "Application Server", "MES Terminal", "Engineering Workstation" }, 150_000, 3_000_000, new[] { "IT Server Room", "Admin Block" }, new[] { "Dell", "Cisco", "HPE" }),
            ["BLD"] = (new[] { "Paint Shop Building", "Utility Block", "Warehouse Shed" }, 80_000_000, 400_000_000, new[] { "Warehouse", "Paint Shop" }, new[] { "L&T Construction" }),
            ["FUR"] = (new[] { "Control Room Furniture Set", "Office Workstations", "Training Room Fixtures" }, 60_000, 900_000, new[] { "Admin Block" }, new[] { "Godrej Interio" }),
        };

        var todayMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var cutoff = todayMonth.AddMonths(-6); // months before this are back-filled; the last 6 are posted via the real service
        var seq = 0;
        var assets = new List<Asset>();
        foreach (var (code, spec) in catalog)
        {
            var cat = cats.First(c => c.Code == code);
            var count = code is "BLD" or "FUR" ? 2 : 5;
            for (var i = 0; i < count; i++)
            {
                var plant = plants[rnd.Next(plants.Count)];
                var area = spec.Areas[rnd.Next(spec.Areas.Length)];
                var cc = code == "ITE" ? corpIt : plant.CostCenters.First(c => c.Code.EndsWith(code == "TST" ? "QLT" : "PRD"));
                var cost = Math.Round((decimal)(spec.Min + rnd.NextDouble() * (spec.Max - spec.Min)) / 1000m) * 1000m;
                var acq = DateTime.Today.AddDays(-rnd.Next(60, 365 * 7));
                var a = new Asset
                {
                    Name = $"{spec.Names[rnd.Next(spec.Names.Length)]} #{++seq}",
                    CategoryId = cat.Id, PlantId = plant.Id, CostCenterId = cc.Id,
                    LocationId = plant.Locations.First(l => l.Name == area).Id,
                    Manufacturer = spec.Makes[rnd.Next(spec.Makes.Length)], SerialNumber = $"SN{rnd.Next(100000, 999999)}",
                    Vendor = "Approved Vendor " + rnd.Next(1, 9), PurchaseOrderNumber = $"PO-{acq:yyyy}-{rnd.Next(1000, 9999)}",
                    AcquisitionDate = acq.Date, AcquisitionCost = cost, Custodian = "Production Supervisor",
                    Criticality = (Criticality)rnd.Next(1, 5), Condition = AssetCondition.Good,
                    Status = AssetStatus.Active, CapitalizationDate = acq.Date, ApprovedBy = "finance.manager@gkm.com",
                    ApprovedAt = acq, CreatedBy = "accountant@gkm.com", CreatedAt = acq, SubmittedAt = acq,
                    WarrantyExpiry = acq.AddYears(rnd.Next(1, 6)),
                };
                AssetService.ApplyCategoryDefaults(a, cat);
                a.AssetTag = $"GKM-{plant.Code}-{code}-{assets.Count(x => x.PlantId == plant.Id && x.CategoryId == cat.Id) + 1:D5}";
                assets.Add(a);
            }
        }

        // Back-fill accumulated depreciation for months before the cutoff using the same calculator the real run uses.
        foreach (var a in assets)
            for (var m = new DateTime(a.CapitalizationDate!.Value.Year, a.CapitalizationDate.Value.Month, 1); m < cutoff; m = m.AddMonths(1))
                a.AccumulatedDepreciation += DepreciationCalculator.MonthlyAmount(a);

        // Workflow states for demo purposes.
        for (var i = 0; i < 2; i++) // pending approval
        {
            var a = assets[^(i + 1)];
            a.Status = AssetStatus.PendingApproval; a.CapitalizationDate = null; a.ApprovedBy = null; a.ApprovedAt = null;
            a.AccumulatedDepreciation = 0; a.AcquisitionDate = DateTime.Today.AddDays(-5); a.SubmittedAt = DateTime.UtcNow.AddDays(-2);
            a.CreatedAt = DateTime.UtcNow.AddDays(-3);
        }
        var draft = assets[^3]; draft.Status = AssetStatus.Draft; draft.CapitalizationDate = null; draft.ApprovedBy = null; draft.ApprovedAt = null; draft.AccumulatedDepreciation = 0; draft.SubmittedAt = null;
        var rejected = assets[^4]; rejected.Status = AssetStatus.Rejected; rejected.CapitalizationDate = null; rejected.AccumulatedDepreciation = 0; rejected.RejectionReason = "Invoice value does not match PO; please attach corrected invoice.";
        var disposed = assets[3]; disposed.Status = AssetStatus.Disposed; disposed.DisposalDate = DateTime.Today.AddDays(-40); disposed.DisposalProceeds = Math.Round(disposed.BookValue * 0.6m); disposed.DisposalReason = "Replaced by newer model";
        var uc = assets.Where(a => a.Status == AssetStatus.Active).Skip(10).Take(2).ToList(); uc.ForEach(a => a.Status = AssetStatus.UnderMaintenance);

        db.Assets.AddRange(assets);
        await db.SaveChangesAsync();

        foreach (var a in assets)
        {
            assetSvc.Log(a.Id, "Created", $"Asset registered ({a.AssetTag})", a.CreatedBy);
            if (a.ApprovedBy != null) assetSvc.Log(a.Id, "Approved", "Capitalised and approved by Finance", a.ApprovedBy);
        }
        assetSvc.Log(disposed.Id, "Disposed", $"Disposed for {disposed.DisposalProceeds:N2}", "finance.manager@gkm.com");
        await db.SaveChangesAsync();

        // Post the most recent six closed months through the real depreciation service.
        var dep = new DepreciationService(db);
        for (var m = cutoff; m < todayMonth; m = m.AddMonths(1))
            await dep.RunAsync(m.Year, m.Month, "finance.manager@gkm.com");

        // Movements
        var movable = assets.Where(a => a.Status == AssetStatus.Active).Take(4).ToList();
        foreach (var a in movable)
        {
            var toPlant = plants.First(p => p.Id != a.PlantId);
            var toLoc = toPlant.Locations.First(l => l.Name == "Warehouse");
            var toCc = toPlant.CostCenters.First(c => c.Code.EndsWith("PRD"));
            db.AssetMovements.Add(new AssetMovement
            {
                AssetId = a.Id, FromPlantId = a.PlantId, FromLocationId = a.LocationId, FromCostCenterId = a.CostCenterId, FromCustodian = a.Custodian,
                ToPlantId = toPlant.Id, ToLocationId = toLoc.Id, ToCostCenterId = toCc.Id, ToCustodian = "Stores In-charge",
                Reason = "Inter-plant transfer for capacity rebalancing", MovedOn = DateTime.UtcNow.AddDays(-rnd.Next(10, 120)), MovedBy = "plant.manager@gkm.com"
            });
            a.PlantId = toPlant.Id; a.LocationId = toLoc.Id; a.CostCenterId = toCc.Id; a.Custodian = "Stores In-charge";
            assetSvc.Log(a.Id, "Transferred", $"Moved to {toPlant.Code} / {toLoc.Name}, cost centre {toCc.Code}", "plant.manager@gkm.com");
        }
        await db.SaveChangesAsync();

        // Maintenance plans and work orders
        var active = assets.Where(a => a.Status is AssetStatus.Active or AssetStatus.UnderMaintenance).ToList();
        var plansList = new List<MaintenancePlan>();
        foreach (var a in active.Take(12))
        {
            var freq = new[] { 30, 60, 90, 180 }[rnd.Next(4)];
            plansList.Add(new MaintenancePlan
            {
                AssetId = a.Id, Name = $"{freq}-day preventive service", FrequencyDays = freq,
                Instructions = "Lubrication, calibration check, safety interlock test, filter replacement.",
                NextDueDate = DateTime.Today.AddDays(rnd.Next(-20, 60)), LastPerformedOn = DateTime.Today.AddDays(-freq),
                EstimatedCost = rnd.Next(5, 60) * 1000
            });
        }
        db.MaintenancePlans.AddRange(plansList);
        await db.SaveChangesAsync();

        var titles = new[] { "Hydraulic leak repair", "Replace worn bearings", "Control panel fault", "Annual calibration", "Preventive service", "Coolant pump replacement", "Safety interlock inspection", "Software update and backup" };
        for (var i = 0; i < 16; i++)
        {
            var a = active[rnd.Next(active.Count)];
            var status = i < 3 ? WorkOrderStatus.Open : i < 5 ? WorkOrderStatus.InProgress : WorkOrderStatus.Completed;
            var requested = DateTime.UtcNow.AddDays(-rnd.Next(2, 150));
            var wo = new WorkOrder
            {
                Number = await assetSvc.NextWorkOrderNumberAsync(), AssetId = a.Id, Title = titles[rnd.Next(titles.Length)],
                Type = (WorkOrderType)rnd.Next(1, 5), Priority = (Priority)rnd.Next(1, 5), Status = status,
                AssignedTo = "maintenance@gkm.com", RequestedBy = "plant.manager@gkm.com", RequestedOn = requested,
                ScheduledDate = requested.AddDays(3).Date,
            };
            if (status != WorkOrderStatus.Open) wo.StartedOn = requested.AddDays(2);
            if (status == WorkOrderStatus.Completed)
            {
                wo.CompletedOn = requested.AddDays(rnd.Next(3, 8));
                wo.LaborCost = rnd.Next(2, 20) * 1000; wo.PartsCost = rnd.Next(0, 80) * 1000; wo.ExternalCost = rnd.Next(0, 3) == 0 ? rnd.Next(10, 90) * 1000 : 0;
                wo.ResolutionNotes = "Completed and verified; machine returned to production.";
            }
            db.WorkOrders.Add(wo);
            await db.SaveChangesAsync();
            assetSvc.Log(a.Id, "WorkOrder", $"{wo.Number} {wo.Status}: {wo.Title}", wo.RequestedBy);
        }
        await db.SaveChangesAsync();

        // One in-progress audit campaign at Bengaluru with partial results
        var blr = plants.First(p => p.Code == "BLR");
        var campaign = new AuditCampaign { Name = $"{DateTime.Today.Year} Annual Physical Verification - {blr.City}", PlantId = blr.Id, Status = AuditCampaignStatus.InProgress, StartDate = DateTime.Today.AddDays(-3), EndDate = DateTime.Today.AddDays(11), CreatedBy = "auditor@gkm.com" };
        var blrAssets = await db.Assets.Where(a => a.PlantId == blr.Id && (a.Status == AssetStatus.Active || a.Status == AssetStatus.UnderMaintenance)).ToListAsync();
        foreach (var a in blrAssets) campaign.Items.Add(new AuditItem { Asset = a, ExpectedLocationId = a.LocationId });
        var k = 0;
        foreach (var item in campaign.Items.Take(Math.Max(1, campaign.Items.Count * 2 / 3)))
        {
            item.Result = k % 7 == 3 ? AuditResult.Missing : k % 5 == 2 ? AuditResult.ConditionIssue : AuditResult.Verified;
            if (item.Result == AuditResult.ConditionIssue) { item.FoundCondition = AssetCondition.Poor; item.Notes = "Visible corrosion on housing."; }
            if (item.Result == AuditResult.Missing) item.Notes = "Not found at expected location.";
            item.VerifiedBy = "auditor@gkm.com"; item.VerifiedOn = DateTime.UtcNow.AddDays(-1);
            k++;
        }
        db.AuditCampaigns.Add(campaign);
        await db.SaveChangesAsync();
    }
}

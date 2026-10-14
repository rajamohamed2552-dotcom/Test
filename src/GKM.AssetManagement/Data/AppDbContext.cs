using GKM.AssetManagement.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GKM.AssetManagement.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Plant> Plants => Set<Plant>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<CostCenter> CostCenters => Set<CostCenter>();
    public DbSet<GlAccount> GlAccounts => Set<GlAccount>();
    public DbSet<AssetCategory> AssetCategories => Set<AssetCategory>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<AssetMovement> AssetMovements => Set<AssetMovement>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<MaintenancePlan> MaintenancePlans => Set<MaintenancePlan>();
    public DbSet<DepreciationRun> DepreciationRuns => Set<DepreciationRun>();
    public DbSet<DepreciationEntry> DepreciationEntries => Set<DepreciationEntry>();
    public DbSet<AuditCampaign> AuditCampaigns => Set<AuditCampaign>();
    public DbSet<AuditItem> AuditItems => Set<AuditItem>();
    public DbSet<AssetEvent> AssetEvents => Set<AssetEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Keep our tables out of "public" so they can't collide with other apps in a shared Postgres database.
        if (Database.IsNpgsql()) b.HasDefaultSchema("gkm");

        // A financial register must never cascade-delete history: restrict all FKs except Identity's own.
        foreach (var fk in b.Model.GetEntityTypes()
                     .Where(e => e.ClrType.Namespace == typeof(Asset).Namespace)
                     .SelectMany(e => e.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;

        foreach (var p in b.Model.GetEntityTypes().SelectMany(e => e.GetProperties())
                     .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            p.SetPrecision(18);
            p.SetScale(2);
        }

        b.Entity<Plant>().HasIndex(x => x.Code).IsUnique();
        b.Entity<CostCenter>().HasIndex(x => x.Code).IsUnique();
        b.Entity<GlAccount>().HasIndex(x => x.Code).IsUnique();
        b.Entity<AssetCategory>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Asset>().HasIndex(x => x.AssetTag).IsUnique();
        b.Entity<Asset>().HasIndex(x => x.SerialNumber);
        b.Entity<Asset>().HasIndex(x => x.Status);
        b.Entity<Asset>().Ignore(x => x.BookValue);
        b.Entity<WorkOrder>().Ignore(x => x.TotalCost);
        b.Entity<WorkOrder>().HasIndex(x => x.Number).IsUnique();
        b.Entity<DepreciationRun>().HasIndex(x => new { x.Year, x.Month }).IsUnique();
        b.Entity<DepreciationRun>().Ignore(x => x.Period);
        // Guarantees an asset can only be depreciated once per period.
        b.Entity<DepreciationEntry>().HasIndex(x => new { x.AssetId, x.Year, x.Month }).IsUnique();
        b.Entity<Location>().Ignore(x => x.FullName);
        b.Entity<CostCenter>().Ignore(x => x.Display);
        b.Entity<GlAccount>().Ignore(x => x.Display);

        // Several navigations to the same principal need explicit wiring.
        b.Entity<AssetMovement>().HasOne(x => x.FromPlant).WithMany().HasForeignKey(x => x.FromPlantId);
        b.Entity<AssetMovement>().HasOne(x => x.ToPlant).WithMany().HasForeignKey(x => x.ToPlantId);
        b.Entity<AssetMovement>().HasOne(x => x.FromLocation).WithMany().HasForeignKey(x => x.FromLocationId);
        b.Entity<AssetMovement>().HasOne(x => x.ToLocation).WithMany().HasForeignKey(x => x.ToLocationId);
        b.Entity<AssetMovement>().HasOne(x => x.FromCostCenter).WithMany().HasForeignKey(x => x.FromCostCenterId);
        b.Entity<AssetMovement>().HasOne(x => x.ToCostCenter).WithMany().HasForeignKey(x => x.ToCostCenterId);
        b.Entity<AssetMovement>().HasOne(x => x.Asset).WithMany(a => a.Movements).HasForeignKey(x => x.AssetId);

        b.Entity<AssetCategory>().HasOne(x => x.AssetGlAccount).WithMany().HasForeignKey(x => x.AssetGlAccountId);
        b.Entity<AssetCategory>().HasOne(x => x.AccumDepGlAccount).WithMany().HasForeignKey(x => x.AccumDepGlAccountId);
        b.Entity<AssetCategory>().HasOne(x => x.DepExpenseGlAccount).WithMany().HasForeignKey(x => x.DepExpenseGlAccountId);

        b.Entity<AuditItem>().HasOne(x => x.ExpectedLocation).WithMany().HasForeignKey(x => x.ExpectedLocationId);
        b.Entity<AuditItem>().HasOne(x => x.FoundLocation).WithMany().HasForeignKey(x => x.FoundLocationId);
        b.Entity<AuditItem>().HasIndex(x => new { x.AuditCampaignId, x.AssetId }).IsUnique();
    }
}

using GKM.AssetManagement.Data;
using GKM.AssetManagement.Models;
using GKM.AssetManagement.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GKM.AssetManagement.Tests;

public class DepreciationCalculatorTests
{
    private static Asset Make(DepreciationMethod m, decimal cost = 120_000, decimal salvage = 12_000, int life = 36) =>
        new() { Method = m, AcquisitionCost = cost, SalvageValue = salvage, UsefulLifeMonths = life };

    [Fact]
    public void StraightLine_IsCostLessSalvageOverLife()
        => Assert.Equal(3_000m, DepreciationCalculator.MonthlyAmount(Make(DepreciationMethod.StraightLine)));

    [Fact]
    public void DecliningBalance_UsesDoubleRateOnOpeningBookValue()
        => Assert.Equal(6_666.67m, DepreciationCalculator.MonthlyAmount(Make(DepreciationMethod.DecliningBalance))); // 120,000 * 2 / 36

    [Fact]
    public void NoDepreciation_IsZero()
        => Assert.Equal(0m, DepreciationCalculator.MonthlyAmount(Make(DepreciationMethod.NoDepreciation)));

    [Fact]
    public void NeverDepreciatesBelowSalvage()
    {
        var a = Make(DepreciationMethod.StraightLine);
        a.AccumulatedDepreciation = 107_000m; // book value 13,000, only 1,000 left above salvage
        Assert.Equal(1_000m, DepreciationCalculator.MonthlyAmount(a));
        a.AccumulatedDepreciation = 108_000m;
        Assert.Equal(0m, DepreciationCalculator.MonthlyAmount(a));
    }

    [Fact]
    public void FullLife_StraightLine_EndsExactlyAtSalvage()
    {
        var a = Make(DepreciationMethod.StraightLine);
        for (var i = 0; i < 60; i++) a.AccumulatedDepreciation += DepreciationCalculator.MonthlyAmount(a);
        Assert.Equal(a.SalvageValue, a.BookValue);
    }
}

public class DepreciationServiceTests : IDisposable
{
    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private readonly AppDbContext _db;

    public DepreciationServiceTests()
    {
        _conn.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();
        var plant = new Plant { Code = "T01", Name = "Test" };
        var cc = new CostCenter { Code = "CC1", Name = "Test CC" };
        var cat = new AssetCategory { Code = "TST", Name = "Test" };
        _db.AddRange(plant, cc, cat);
        _db.SaveChanges();
        var thisMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _db.Assets.AddRange(
            NewAsset("A1", AssetStatus.Active, thisMonth.AddMonths(-2), plant, cc, cat),
            NewAsset("A2", AssetStatus.Draft, null, plant, cc, cat),                       // not approved: must be skipped
            NewAsset("A3", AssetStatus.Disposed, thisMonth.AddMonths(-5), plant, cc, cat)); // disposed: must be skipped
        _db.SaveChanges();
    }

    private static Asset NewAsset(string tag, AssetStatus s, DateTime? cap, Plant p, CostCenter c, AssetCategory cat) => new()
    {
        AssetTag = tag, Name = tag, Status = s, CapitalizationDate = cap, Plant = p, CostCenter = c, Category = cat,
        AcquisitionCost = 120_000, SalvageValue = 12_000, UsefulLifeMonths = 36, Method = DepreciationMethod.StraightLine
    };

    [Fact]
    public async Task Run_PostsOnlyForApprovedLiveAssets()
    {
        var svc = new DepreciationService(_db);
        var run = await svc.RunAsync(DateTime.Today.Year, DateTime.Today.Month, "tester");
        Assert.Equal(1, run.AssetsProcessed);
        Assert.Equal(3_000m, run.TotalDepreciation);
        Assert.Equal(3_000m, (await _db.Assets.FirstAsync(a => a.AssetTag == "A1")).AccumulatedDepreciation);
        Assert.Equal(0m, (await _db.Assets.FirstAsync(a => a.AssetTag == "A2")).AccumulatedDepreciation);
    }

    [Fact]
    public async Task Run_CannotRepeatSamePeriod_OrRunFuturePeriod()
    {
        var svc = new DepreciationService(_db);
        await svc.RunAsync(DateTime.Today.Year, DateTime.Today.Month, "tester");
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RunAsync(DateTime.Today.Year, DateTime.Today.Month, "tester"));
        var future = DateTime.Today.AddMonths(2);
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RunAsync(future.Year, future.Month, "tester"));
    }

    public void Dispose() { _db.Dispose(); _conn.Dispose(); }
}

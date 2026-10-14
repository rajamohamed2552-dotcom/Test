using GKM.AssetManagement.Data;
using GKM.AssetManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace GKM.AssetManagement.Services;

public static class DepreciationCalculator
{
    /// <summary>Depreciation for one month, never taking book value below salvage value.</summary>
    public static decimal MonthlyAmount(Asset a)
    {
        if (a.Method == DepreciationMethod.NoDepreciation || a.UsefulLifeMonths <= 0) return 0;

        var bookValue = a.AcquisitionCost - a.AccumulatedDepreciation;
        var remaining = bookValue - a.SalvageValue;
        if (remaining <= 0) return 0;

        decimal amount = a.Method switch
        {
            DepreciationMethod.StraightLine => (a.AcquisitionCost - a.SalvageValue) / a.UsefulLifeMonths,
            // Double-declining balance: annual rate = 2 / life in years, applied monthly to opening book value.
            DepreciationMethod.DecliningBalance => bookValue * 2m / a.UsefulLifeMonths,
            _ => 0
        };
        return Math.Round(Math.Min(amount, remaining), 2);
    }
}

public class DepreciationService
{
    private readonly AppDbContext _db;
    public DepreciationService(AppDbContext db) => _db = db;

    public static bool IsDepreciable(Asset a) =>
        (a.Status == AssetStatus.Active || a.Status == AssetStatus.UnderMaintenance) && a.CapitalizationDate != null;

    /// <summary>Posts depreciation for every eligible asset for the given period. Throws if the period was already run.</summary>
    public async Task<DepreciationRun> RunAsync(int year, int month, string? user)
    {
        var periodStart = new DateTime(year, month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);
        if (periodStart > new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1))
            throw new InvalidOperationException("Depreciation cannot be run for a future period.");
        if (await _db.DepreciationRuns.AnyAsync(r => r.Year == year && r.Month == month))
            throw new InvalidOperationException($"Depreciation for {periodStart:MMM yyyy} has already been posted.");

        var assets = await _db.Assets
            .Include(a => a.Category)
            .Where(a => (a.Status == AssetStatus.Active || a.Status == AssetStatus.UnderMaintenance)
                        && a.CapitalizationDate != null && a.CapitalizationDate <= periodEnd)
            .ToListAsync();

        var run = new DepreciationRun { Year = year, Month = month, RunBy = user };
        foreach (var a in assets)
        {
            var amount = DepreciationCalculator.MonthlyAmount(a);
            if (amount <= 0) continue;
            a.AccumulatedDepreciation += amount;
            run.Entries.Add(new DepreciationEntry
            {
                AssetId = a.Id, Year = year, Month = month, Amount = amount,
                AccumulatedAfter = a.AccumulatedDepreciation, BookValueAfter = a.BookValue,
                CostCenterId = a.CostCenterId, DepExpenseGlAccountId = a.Category?.DepExpenseGlAccountId
            });
            _db.AssetEvents.Add(new AssetEvent
            {
                AssetId = a.Id, EventType = "Depreciation", PerformedBy = user,
                Details = $"{periodStart:MMM yyyy}: {amount:N2} posted, book value {a.BookValue:N2}"
            });
        }
        run.AssetsProcessed = run.Entries.Count;
        run.TotalDepreciation = run.Entries.Sum(e => e.Amount);
        _db.DepreciationRuns.Add(run);
        await _db.SaveChangesAsync();
        return run;
    }
}

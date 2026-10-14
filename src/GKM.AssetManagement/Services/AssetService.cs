using GKM.AssetManagement.Data;
using GKM.AssetManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace GKM.AssetManagement.Services;

public class AssetService
{
    private readonly AppDbContext _db;
    public AssetService(AppDbContext db) => _db = db;

    /// <summary>Tag format: GKM-{plant}-{category}-{5 digit sequence}, e.g. GKM-BLR-MCH-00042.</summary>
    public async Task<string> NextTagAsync(int plantId, int categoryId)
    {
        var plant = await _db.Plants.AsNoTracking().FirstAsync(p => p.Id == plantId);
        var cat = await _db.AssetCategories.AsNoTracking().FirstAsync(c => c.Id == categoryId);
        var prefix = $"GKM-{plant.Code}-{cat.Code}-";
        var existing = await _db.Assets.Where(a => a.AssetTag.StartsWith(prefix)).Select(a => a.AssetTag).ToListAsync();
        var max = existing
            .Select(t => int.TryParse(t[prefix.Length..], out var n) ? n : 0)
            .DefaultIfEmpty(0).Max();
        return $"{prefix}{max + 1:D5}";
    }

    public void Log(int assetId, string type, string details, string? user) =>
        _db.AssetEvents.Add(new AssetEvent { AssetId = assetId, EventType = type, Details = details, PerformedBy = user });

    public void Log(Asset asset, string type, string details, string? user) =>
        _db.AssetEvents.Add(new AssetEvent { Asset = asset, EventType = type, Details = details, PerformedBy = user });

    /// <summary>Applies the category's default depreciation settings to an asset.</summary>
    public static void ApplyCategoryDefaults(Asset asset, AssetCategory cat)
    {
        asset.Method = cat.DefaultMethod;
        asset.UsefulLifeMonths = cat.DefaultUsefulLifeMonths;
        asset.SalvageValue = Math.Round(asset.AcquisitionCost * cat.DefaultSalvagePercent / 100m, 2);
    }

    public async Task<string> NextWorkOrderNumberAsync()
    {
        var year = DateTime.Today.Year;
        var prefix = $"WO-{year}-";
        var existing = await _db.WorkOrders.Where(w => w.Number.StartsWith(prefix)).Select(w => w.Number).ToListAsync();
        var max = existing.Select(n => int.TryParse(n[prefix.Length..], out var v) ? v : 0).DefaultIfEmpty(0).Max();
        return $"{prefix}{max + 1:D5}";
    }
}

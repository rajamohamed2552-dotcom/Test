using GKM.AssetManagement.Data;
using GKM.AssetManagement.Models;
using GKM.AssetManagement.Services;
using GKM.AssetManagement.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GKM.AssetManagement.Controllers;

/// <summary>Asset tracking: location / cost-centre / custodian transfers with a full movement history.</summary>
public class MovementsController : AppController
{
    public MovementsController(AppDbContext db, AssetService assets) : base(db, assets) { }

    public async Task<IActionResult> Index(int? assetId)
    {
        var q = Db.AssetMovements.AsNoTracking()
            .Include(m => m.Asset).Include(m => m.FromPlant).Include(m => m.ToPlant)
            .Include(m => m.FromLocation).Include(m => m.ToLocation).Include(m => m.FromCostCenter).Include(m => m.ToCostCenter).AsQueryable();
        if (assetId != null) q = q.Where(m => m.AssetId == assetId);
        ViewBag.AssetId = assetId;
        return View(await q.OrderByDescending(m => m.MovedOn).Take(200).ToListAsync());
    }

    /// <summary>Where is everything right now? Location-level headcount per plant.</summary>
    public async Task<IActionResult> Where()
    {
        var rows = await Db.Assets.AsNoTracking().Where(a => a.Status == AssetStatus.Active || a.Status == AssetStatus.UnderMaintenance)
            .Select(a => new { Plant = a.Plant!.Code, Location = a.Location != null ? a.Location.Name : "(unassigned)", a.Status }).ToListAsync();
        ViewBag.Rows = rows.GroupBy(r => (r.Plant, r.Location)).OrderBy(g => g.Key.Plant).ThenBy(g => g.Key.Location)
            .Select(g => (g.Key.Plant, g.Key.Location, g.Count(), g.Count(x => x.Status == AssetStatus.UnderMaintenance))).ToList();
        return View();
    }

    [Authorize(Roles = Roles.AssetEditors)]
    public async Task<IActionResult> Create(int? assetId)
    {
        await LoadMoveLookups();
        return View(new MoveForm { AssetId = assetId ?? 0 });
    }

    [Authorize(Roles = Roles.AssetEditors), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(MoveForm f)
    {
        var a = await Db.Assets.FindAsync(f.AssetId);
        if (a == null || a.Status is not (AssetStatus.Active or AssetStatus.UnderMaintenance))
            ModelState.AddModelError(nameof(f.AssetId), "Only active assets can be transferred.");
        if (f.ToLocationId != null && !await Db.Locations.AnyAsync(l => l.Id == f.ToLocationId && l.PlantId == f.ToPlantId))
            ModelState.AddModelError(nameof(f.ToLocationId), "Location does not belong to the selected plant.");
        if (a != null && a.PlantId == f.ToPlantId && a.LocationId == f.ToLocationId && a.CostCenterId == f.ToCostCenterId && (a.Custodian ?? "") == (f.ToCustodian ?? a.Custodian ?? ""))
            ModelState.AddModelError("", "Nothing changes with this transfer.");
        if (!ModelState.IsValid || a == null) { await LoadMoveLookups(); return View(f); }

        var m = new AssetMovement
        {
            AssetId = a.Id, FromPlantId = a.PlantId, FromLocationId = a.LocationId, FromCostCenterId = a.CostCenterId, FromCustodian = a.Custodian,
            ToPlantId = f.ToPlantId, ToLocationId = f.ToLocationId, ToCostCenterId = f.ToCostCenterId, ToCustodian = string.IsNullOrWhiteSpace(f.ToCustodian) ? a.Custodian : f.ToCustodian,
            Reason = f.Reason, MovedBy = UserName
        };
        var ccChanged = a.CostCenterId != f.ToCostCenterId;
        a.PlantId = m.ToPlantId; a.LocationId = m.ToLocationId; a.CostCenterId = m.ToCostCenterId; a.Custodian = m.ToCustodian;
        Db.AssetMovements.Add(m);
        var plant = await Db.Plants.FindAsync(f.ToPlantId);
        var loc = f.ToLocationId == null ? null : await Db.Locations.FindAsync(f.ToLocationId);
        var cc = await Db.CostCenters.FindAsync(f.ToCostCenterId);
        Assets.Log(a, "Transferred", $"Moved to {plant?.Code} / {loc?.Name ?? "(no location)"}, cost centre {cc?.Code}{(ccChanged ? " (future depreciation follows new cost centre)" : "")}. Reason: {f.Reason}", UserName);
        await Db.SaveChangesAsync();
        Flash($"{a.AssetTag} transferred.");
        return RedirectToAction("Details", "Assets", new { a.Id });
    }

    private async Task LoadMoveLookups()
    {
        await LoadAssetLookupsAsync();
        ViewBag.AssetList = await Db.Assets.AsNoTracking().Where(a => a.Status == AssetStatus.Active || a.Status == AssetStatus.UnderMaintenance).OrderBy(a => a.AssetTag).ToListAsync();
    }
}

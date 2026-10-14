using System.Text;
using GKM.AssetManagement.Data;
using GKM.AssetManagement.Models;
using GKM.AssetManagement.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GKM.AssetManagement.Controllers;

/// <summary>Physical verification campaigns: auditors confirm existence, location and condition of each asset.</summary>
public class AuditsController : AppController
{
    public AuditsController(AppDbContext db, AssetService assets) : base(db, assets) { }

    public async Task<IActionResult> Index()
    {
        var campaigns = await Db.AuditCampaigns.AsNoTracking().Include(c => c.Plant).Include(c => c.Items).ToListAsync();
        return View(campaigns.OrderByDescending(c => c.StartDate).ToList());
    }

    [Authorize(Roles = Roles.AuditUsers)]
    public async Task<IActionResult> Create()
    {
        await LoadAssetLookupsAsync();
        return View(new AuditCampaign());
    }

    [Authorize(Roles = Roles.AuditUsers), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Name,PlantId,StartDate,EndDate")] AuditCampaign c)
    {
        ModelState.Remove(nameof(c.Plant));
        if (c.EndDate < c.StartDate) ModelState.AddModelError(nameof(c.EndDate), "End date must be on or after the start date.");
        if (!ModelState.IsValid) { await LoadAssetLookupsAsync(); return View(c); }

        // Snapshot the in-scope population (live assets at the plant) with their expected locations.
        var scope = await Db.Assets.Where(a => a.PlantId == c.PlantId && (a.Status == AssetStatus.Active || a.Status == AssetStatus.UnderMaintenance)).ToListAsync();
        if (scope.Count == 0) { ModelState.AddModelError(nameof(c.PlantId), "No active assets at this plant."); await LoadAssetLookupsAsync(); return View(c); }
        c.CreatedBy = UserName; c.Status = AuditCampaignStatus.InProgress;
        foreach (var a in scope) c.Items.Add(new AuditItem { AssetId = a.Id, ExpectedLocationId = a.LocationId });
        Db.AuditCampaigns.Add(c);
        await Db.SaveChangesAsync();
        Flash($"Audit campaign created with {scope.Count} assets in scope.");
        return RedirectToAction(nameof(Details), new { c.Id });
    }

    public async Task<IActionResult> Details(int id, AuditResult? result)
    {
        var c = await Db.AuditCampaigns.Include(x => x.Plant).FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound();
        var items = await Db.AuditItems.AsNoTracking().Include(i => i.Asset).ThenInclude(a => a!.Category).Include(i => i.ExpectedLocation).Include(i => i.FoundLocation)
            .Where(i => i.AuditCampaignId == id).ToListAsync();
        ViewBag.All = items;
        ViewBag.Items = items.Where(i => result == null || i.Result == result).OrderBy(i => i.Asset!.AssetTag).ToList();
        ViewBag.Filter = result;
        ViewBag.Locations = await Db.Locations.AsNoTracking().Where(l => l.PlantId == c.PlantId).OrderBy(l => l.Name).ToListAsync();
        return View(c);
    }

    [Authorize(Roles = Roles.AuditUsers), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Verify(int itemId, AuditResult result, int? foundLocationId, AssetCondition? condition, string? notes)
    {
        var item = await Db.AuditItems.Include(i => i.Campaign).Include(i => i.Asset).FirstOrDefaultAsync(i => i.Id == itemId);
        if (item == null) return NotFound();
        if (item.Campaign!.Status == AuditCampaignStatus.Closed) { Flash("This campaign is closed.", true); return RedirectToAction(nameof(Details), new { id = item.AuditCampaignId }); }
        if (result == AuditResult.Pending) { Flash("Choose an outcome.", true); return RedirectToAction(nameof(Details), new { id = item.AuditCampaignId }); }
        if (result != AuditResult.Verified && string.IsNullOrWhiteSpace(notes)) { Flash("Notes are required for any finding.", true); return RedirectToAction(nameof(Details), new { id = item.AuditCampaignId }); }
        if (result == AuditResult.LocationMismatch && foundLocationId == null) { Flash("Select where the asset was actually found.", true); return RedirectToAction(nameof(Details), new { id = item.AuditCampaignId }); }

        item.Result = result; item.Notes = notes?.Trim(); item.VerifiedBy = UserName; item.VerifiedOn = DateTime.UtcNow;
        item.FoundLocationId = result == AuditResult.LocationMismatch ? foundLocationId : item.ExpectedLocationId;
        item.FoundCondition = condition;
        if (result != AuditResult.Verified)
            Assets.Log(item.AssetId, "Audit", $"{item.Campaign.Name}: {result}. {item.Notes}", UserName);
        await Db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id = item.AuditCampaignId, result = (AuditResult?)null });
    }

    [Authorize(Roles = Roles.AuditUsers), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(int id, string? summary)
    {
        var c = await Db.AuditCampaigns.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound();
        var pending = c.Items.Count(i => i.Result == AuditResult.Pending);
        if (pending > 0) { Flash($"{pending} asset(s) are still unverified. Verify or record a finding for each before closing.", true); return RedirectToAction(nameof(Details), new { id }); }
        c.Status = AuditCampaignStatus.Closed; c.ClosedOn = DateTime.UtcNow; c.Summary = summary?.Trim();
        await Db.SaveChangesAsync();
        Flash("Audit campaign closed.");
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Export(int id)
    {
        var c = await Db.AuditCampaigns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound();
        var items = await Db.AuditItems.AsNoTracking().Include(i => i.Asset).Include(i => i.ExpectedLocation).Include(i => i.FoundLocation).Where(i => i.AuditCampaignId == id).ToListAsync();
        var sb = new StringBuilder("AssetTag,Asset,BookValue,ExpectedLocation,Result,FoundLocation,Condition,Notes,VerifiedBy,VerifiedOn\n");
        foreach (var i in items.OrderBy(i => i.Asset!.AssetTag))
            sb.AppendLine(string.Join(',', Csv.Esc(i.Asset!.AssetTag), Csv.Esc(i.Asset.Name), i.Asset.BookValue, Csv.Esc(i.ExpectedLocation?.Name), i.Result, Csv.Esc(i.FoundLocation?.Name), i.FoundCondition, Csv.Esc(i.Notes), Csv.Esc(i.VerifiedBy), i.VerifiedOn?.ToString("yyyy-MM-dd")));
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"audit-{id}.csv");
    }

    /// <summary>Global immutable activity trail across all assets.</summary>
    public async Task<IActionResult> Trail(string? type, int page = 1)
    {
        var q = Db.AssetEvents.AsNoTracking().Include(e => e.Asset).AsQueryable();
        if (!string.IsNullOrWhiteSpace(type)) q = q.Where(e => e.EventType == type);
        page = Math.Max(1, page);
        ViewBag.Total = await q.CountAsync(); ViewBag.Page = page; ViewBag.Type = type;
        ViewBag.Types = await Db.AssetEvents.Select(e => e.EventType).Distinct().OrderBy(x => x).ToListAsync();
        return View(await q.OrderByDescending(e => e.At).Skip((page - 1) * 50).Take(50).ToListAsync());
    }
}

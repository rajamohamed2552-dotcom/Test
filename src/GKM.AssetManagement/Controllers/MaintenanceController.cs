using GKM.AssetManagement.Data;
using GKM.AssetManagement.Models;
using GKM.AssetManagement.Services;
using GKM.AssetManagement.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GKM.AssetManagement.Controllers;

public class MaintenanceController : AppController
{
    public MaintenanceController(AppDbContext db, AssetService assets) : base(db, assets) { }

    public async Task<IActionResult> Index(WorkOrderStatus? status)
    {
        var q = Db.WorkOrders.AsNoTracking().Include(w => w.Asset).AsQueryable();
        if (status != null) q = q.Where(w => w.Status == status);
        ViewBag.Status = status;
        var list = await q.ToListAsync(); // ordered in memory: portable across providers
        return View(list.OrderBy(w => w.Status is WorkOrderStatus.Completed or WorkOrderStatus.Cancelled).ThenByDescending(w => w.Priority).ThenByDescending(w => w.RequestedOn).Take(300).ToList());
    }

    public async Task<IActionResult> Details(int id)
    {
        var w = await Db.WorkOrders.Include(x => x.Asset).ThenInclude(a => a!.Plant).FirstOrDefaultAsync(x => x.Id == id);
        return w == null ? NotFound() : View(w);
    }

    [Authorize(Roles = Roles.MaintenanceUsers)]
    public async Task<IActionResult> Create(int? assetId)
    {
        await LoadAssets();
        return View(new WorkOrderForm { AssetId = assetId ?? 0 });
    }

    [Authorize(Roles = Roles.MaintenanceUsers), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(WorkOrderForm f)
    {
        var a = await Db.Assets.FindAsync(f.AssetId);
        if (a == null || a.Status is not (AssetStatus.Active or AssetStatus.UnderMaintenance))
            ModelState.AddModelError(nameof(f.AssetId), "Work orders can only be raised against active assets.");
        if (!ModelState.IsValid) { await LoadAssets(); return View(f); }

        var wo = new WorkOrder
        {
            Number = await Assets.NextWorkOrderNumberAsync(), AssetId = f.AssetId, Title = f.Title, Description = f.Description, Type = f.Type, Priority = f.Priority,
            AssignedTo = f.AssignedTo, ScheduledDate = f.ScheduledDate, RequestedBy = UserName
        };
        Db.WorkOrders.Add(wo);
        Assets.Log(a!, "WorkOrder", $"{wo.Number} raised: {wo.Title} ({wo.Type}, {wo.Priority})", UserName);
        await Db.SaveChangesAsync();
        Flash($"Work order {wo.Number} created.");
        return RedirectToAction(nameof(Details), new { wo.Id });
    }

    [Authorize(Roles = Roles.MaintenanceUsers), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(int id)
    {
        var w = await Db.WorkOrders.Include(x => x.Asset).FirstOrDefaultAsync(x => x.Id == id);
        if (w == null || w.Status != WorkOrderStatus.Open) return BadRequest();
        w.Status = WorkOrderStatus.InProgress; w.StartedOn = DateTime.UtcNow; w.AssignedTo ??= UserName;
        if (w.Type == WorkOrderType.Corrective && w.Asset!.Status == AssetStatus.Active) w.Asset.Status = AssetStatus.UnderMaintenance;
        Assets.Log(w.AssetId, "WorkOrder", $"{w.Number} started", UserName);
        await Db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = Roles.MaintenanceUsers)]
    public async Task<IActionResult> Complete(int id)
    {
        var w = await Db.WorkOrders.Include(x => x.Asset).FirstOrDefaultAsync(x => x.Id == id);
        if (w == null || w.Status is not (WorkOrderStatus.Open or WorkOrderStatus.InProgress)) return NotFound();
        ViewBag.WorkOrder = w;
        return View(new CompleteWorkOrderForm { Id = id, Condition = w.Asset!.Condition });
    }

    [Authorize(Roles = Roles.MaintenanceUsers), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(CompleteWorkOrderForm f)
    {
        var w = await Db.WorkOrders.Include(x => x.Asset).Include(x => x.MaintenancePlan).FirstOrDefaultAsync(x => x.Id == f.Id);
        if (w == null || w.Status is not (WorkOrderStatus.Open or WorkOrderStatus.InProgress)) return NotFound();
        if (!ModelState.IsValid) { ViewBag.WorkOrder = w; return View(f); }

        w.Status = WorkOrderStatus.Completed; w.CompletedOn = DateTime.UtcNow; w.StartedOn ??= DateTime.UtcNow;
        w.LaborCost = f.LaborCost; w.PartsCost = f.PartsCost; w.ExternalCost = f.ExternalCost; w.ResolutionNotes = f.ResolutionNotes;
        w.Asset!.Condition = f.Condition;
        if (w.MaintenancePlan != null)
        {
            w.MaintenancePlan.LastPerformedOn = DateTime.Today;
            w.MaintenancePlan.NextDueDate = DateTime.Today.AddDays(w.MaintenancePlan.FrequencyDays);
        }
        await ReleaseAssetIfIdle(w);
        Assets.Log(w.AssetId, "WorkOrder", $"{w.Number} completed; cost {w.TotalCost:N2} (labour {w.LaborCost:N2}, parts {w.PartsCost:N2}, external {w.ExternalCost:N2})", UserName);
        await Db.SaveChangesAsync();
        Flash($"{w.Number} completed. {w.TotalCost:N2} added to the asset's maintenance cost.");
        return RedirectToAction(nameof(Details), new { id = w.Id });
    }

    [Authorize(Roles = Roles.MaintenanceUsers), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var w = await Db.WorkOrders.Include(x => x.Asset).FirstOrDefaultAsync(x => x.Id == id);
        if (w == null || w.Status is not (WorkOrderStatus.Open or WorkOrderStatus.InProgress)) return BadRequest();
        w.Status = WorkOrderStatus.Cancelled;
        await ReleaseAssetIfIdle(w);
        Assets.Log(w.AssetId, "WorkOrder", $"{w.Number} cancelled", UserName);
        await Db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    // ---------- Preventive maintenance plans ----------

    public async Task<IActionResult> Plans()
    {
        var plans = await Db.MaintenancePlans.AsNoTracking().Include(p => p.Asset).Where(p => p.IsActive).ToListAsync();
        return View(plans.OrderBy(p => p.NextDueDate).ToList());
    }

    [Authorize(Roles = Roles.MaintenanceUsers)]
    public async Task<IActionResult> CreatePlan(int? assetId)
    {
        await LoadAssets();
        return View(new MaintenancePlan { AssetId = assetId ?? 0 });
    }

    [Authorize(Roles = Roles.MaintenanceUsers), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePlan([Bind("AssetId,Name,Instructions,FrequencyDays,NextDueDate,EstimatedCost")] MaintenancePlan p)
    {
        if (!await Db.Assets.AnyAsync(a => a.Id == p.AssetId)) ModelState.AddModelError(nameof(p.AssetId), "Select an asset.");
        ModelState.Remove(nameof(p.Asset));
        if (!ModelState.IsValid) { await LoadAssets(); return View(p); }
        Db.MaintenancePlans.Add(p);
        Assets.Log(p.AssetId, "PM Plan", $"Preventive plan '{p.Name}' every {p.FrequencyDays} days", UserName);
        await Db.SaveChangesAsync();
        Flash("Maintenance plan created.");
        return RedirectToAction(nameof(Plans));
    }

    /// <summary>Raises work orders for every active plan due within the next 7 days that has no open order yet.</summary>
    [Authorize(Roles = Roles.MaintenanceUsers), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateDue()
    {
        var horizon = DateTime.Today.AddDays(7);
        var due = await Db.MaintenancePlans.Include(p => p.Asset)
            .Where(p => p.IsActive && p.NextDueDate <= horizon && (p.Asset!.Status == AssetStatus.Active || p.Asset.Status == AssetStatus.UnderMaintenance)).ToListAsync();
        var openPlanIds = await Db.WorkOrders.Where(w => w.MaintenancePlanId != null && (w.Status == WorkOrderStatus.Open || w.Status == WorkOrderStatus.InProgress))
            .Select(w => w.MaintenancePlanId!.Value).ToListAsync();
        var created = 0;
        foreach (var p in due.Where(p => !openPlanIds.Contains(p.Id)))
        {
            var wo = new WorkOrder
            {
                Number = await Assets.NextWorkOrderNumberAsync(), AssetId = p.AssetId, MaintenancePlanId = p.Id, Type = WorkOrderType.Preventive,
                Title = p.Name, Description = p.Instructions, ScheduledDate = p.NextDueDate, Priority = p.NextDueDate < DateTime.Today ? Priority.High : Priority.Medium, RequestedBy = "PM scheduler"
            };
            Db.WorkOrders.Add(wo);
            await Db.SaveChangesAsync(); // persist each so the next number is unique
            Assets.Log(p.AssetId, "WorkOrder", $"{wo.Number} generated from plan '{p.Name}'", UserName);
            created++;
        }
        await Db.SaveChangesAsync();
        Flash(created == 0 ? "No plans are due without an existing open work order." : $"{created} preventive work order(s) generated.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Restore the asset to Active when no other work order is keeping it under maintenance.</summary>
    private async Task ReleaseAssetIfIdle(WorkOrder w)
    {
        if (w.Asset?.Status != AssetStatus.UnderMaintenance) return;
        var others = await Db.WorkOrders.AnyAsync(x => x.AssetId == w.AssetId && x.Id != w.Id && x.Type == WorkOrderType.Corrective && x.Status == WorkOrderStatus.InProgress);
        if (!others) w.Asset.Status = AssetStatus.Active;
    }

    private async Task LoadAssets() =>
        ViewBag.AssetList = await Db.Assets.AsNoTracking().Where(a => a.Status == AssetStatus.Active || a.Status == AssetStatus.UnderMaintenance).OrderBy(a => a.AssetTag).ToListAsync();
}

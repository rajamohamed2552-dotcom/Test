using System.Text;
using GKM.AssetManagement.Data;
using GKM.AssetManagement.Models;
using GKM.AssetManagement.Services;
using GKM.AssetManagement.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace GKM.AssetManagement.Controllers;

public class AssetsController : AppController
{
    private const int PageSize = 20;
    public AssetsController(AppDbContext db, AssetService assets) : base(db, assets) { }

    private IQueryable<Asset> Filtered(string? q, AssetStatus? status, int? plantId, int? categoryId)
    {
        var query = Db.Assets.AsNoTracking().Include(a => a.Category).Include(a => a.Plant).Include(a => a.CostCenter).Include(a => a.Location).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim().ToLower();
            query = query.Where(a => a.AssetTag.ToLower().Contains(t) || a.Name.ToLower().Contains(t) || (a.SerialNumber != null && a.SerialNumber.ToLower().Contains(t)));
        }
        if (status != null) query = query.Where(a => a.Status == status);
        if (plantId != null) query = query.Where(a => a.PlantId == plantId);
        if (categoryId != null) query = query.Where(a => a.CategoryId == categoryId);
        return query;
    }

    public async Task<IActionResult> Index(string? q, AssetStatus? status, int? plantId, int? categoryId, int page = 1)
    {
        var query = Filtered(q, status, plantId, categoryId);
        var total = await query.CountAsync();
        page = Math.Max(1, page);
        ViewBag.Items = await query.OrderBy(a => a.AssetTag).Skip((page - 1) * PageSize).Take(PageSize).ToListAsync();
        ViewBag.Total = total; ViewBag.Page = page; ViewBag.PageSize = PageSize;
        ViewBag.Q = q; ViewBag.Status = status; ViewBag.PlantId = plantId; ViewBag.CategoryId = categoryId;
        await LoadAssetLookupsAsync();
        return View();
    }

    public async Task<IActionResult> Export(string? q, AssetStatus? status, int? plantId, int? categoryId)
    {
        var rows = await Filtered(q, status, plantId, categoryId).OrderBy(a => a.AssetTag).ToListAsync();
        var sb = new StringBuilder("AssetTag,Name,Class,Category,Plant,Location,CostCenter,Status,AcquisitionDate,CapitalizationDate,Cost,AccumulatedDepreciation,BookValue,SerialNumber\n");
        foreach (var a in rows)
            sb.AppendLine(string.Join(',', Csv.Esc(a.AssetTag), Csv.Esc(a.Name), Csv.Esc(a.Category?.AssetClass), Csv.Esc(a.Category?.Name), Csv.Esc(a.Plant?.Code), Csv.Esc(a.Location?.Name),
                Csv.Esc(a.CostCenter?.Code), a.Status, a.AcquisitionDate.ToString("yyyy-MM-dd"), a.CapitalizationDate?.ToString("yyyy-MM-dd"), a.AcquisitionCost, a.AccumulatedDepreciation, a.BookValue, Csv.Esc(a.SerialNumber)));
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"asset-register-{DateTime.Today:yyyyMMdd}.csv");
    }

    public async Task<IActionResult> Details(int id)
    {
        var a = await Db.Assets
            .Include(x => x.Category).ThenInclude(c => c!.AssetGlAccount)
            .Include(x => x.Category).ThenInclude(c => c!.DepExpenseGlAccount)
            .Include(x => x.Category).ThenInclude(c => c!.AccumDepGlAccount)
            .Include(x => x.Plant).Include(x => x.Location).Include(x => x.CostCenter)
            .Include(x => x.Movements).ThenInclude(m => m.ToPlant)
            .Include(x => x.Movements).ThenInclude(m => m.ToLocation)
            .Include(x => x.Movements).ThenInclude(m => m.ToCostCenter)
            .Include(x => x.WorkOrders).Include(x => x.Events)
            .Include(x => x.DepreciationEntries)
            .AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id);
        return a == null ? NotFound() : View(a);
    }

    [Authorize(Roles = Roles.AssetEditors)]
    public async Task<IActionResult> Create()
    {
        await LoadAssetLookupsAsync();
        return View("Form", new AssetForm());
    }

    [Authorize(Roles = Roles.AssetEditors), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AssetForm f)
    {
        var cat = await Db.AssetCategories.FindAsync(f.CategoryId);
        if (cat != null && f.AcquisitionCost < cat.CapitalizationThreshold)
            ModelState.AddModelError(nameof(f.AcquisitionCost), $"Below the {cat.Name} capitalisation threshold ({cat.CapitalizationThreshold:N0}); this should be expensed, not capitalised.");
        if (!ModelState.IsValid || cat == null)
        {
            await LoadAssetLookupsAsync();
            return View("Form", f);
        }

        var a = new Asset { CreatedBy = UserName, Status = AssetStatus.Draft };
        MapFinancials(a, f, cat);
        MapDescriptive(a, f);
        a.AssetTag = await Assets.NextTagAsync(f.PlantId, f.CategoryId);
        Db.Assets.Add(a);
        Assets.Log(a, "Created", $"Asset registered as draft ({a.AssetTag})", UserName);
        await Db.SaveChangesAsync();
        Flash($"Asset {a.AssetTag} created as Draft. Submit it for approval when ready.");
        return RedirectToAction(nameof(Details), new { a.Id });
    }

    [Authorize(Roles = Roles.AssetEditors)]
    public async Task<IActionResult> Edit(int id)
    {
        var a = await Db.Assets.FindAsync(id);
        if (a == null) return NotFound();
        await LoadAssetLookupsAsync();
        ViewBag.Asset = a;
        ViewBag.FinancialsLocked = !IsFinanciallyEditable(a);
        return View("Form", ToForm(a));
    }

    [Authorize(Roles = Roles.AssetEditors), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, AssetForm f)
    {
        var a = await Db.Assets.FindAsync(id);
        if (a == null) return NotFound();
        if (a.Status == AssetStatus.Disposed || a.Status == AssetStatus.PendingApproval)
        {
            Flash($"A {a.Status} asset cannot be edited.", true);
            return RedirectToAction(nameof(Details), new { id });
        }
        var cat = await Db.AssetCategories.FindAsync(IsFinanciallyEditable(a) ? f.CategoryId : a.CategoryId);
        if (!ModelState.IsValid || cat == null)
        {
            await LoadAssetLookupsAsync();
            ViewBag.Asset = a; ViewBag.FinancialsLocked = !IsFinanciallyEditable(a);
            return View("Form", f);
        }

        var changes = new List<string>();
        if (a.Condition != f.Condition) changes.Add($"condition {a.Condition} → {f.Condition}");
        if (a.Custodian != f.Custodian) changes.Add($"custodian '{a.Custodian}' → '{f.Custodian}'");
        if (IsFinanciallyEditable(a))
        {
            if (a.AcquisitionCost != f.AcquisitionCost) changes.Add($"cost {a.AcquisitionCost:N2} → {f.AcquisitionCost:N2}");
            MapFinancials(a, f, cat);
        }
        // Once approved, location/cost-centre changes must go through a formal transfer (see Movements).
        MapDescriptive(a, f, includeOwnership: IsFinanciallyEditable(a));
        a.RejectionReason = null;
        Assets.Log(a, "Edited", changes.Count > 0 ? "Updated: " + string.Join("; ", changes) : "Details updated", UserName);
        await Db.SaveChangesAsync();
        Flash("Asset updated.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = Roles.AssetEditors), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int id)
    {
        var a = await Db.Assets.FindAsync(id);
        if (a == null) return NotFound();
        if (a.Status is not (AssetStatus.Draft or AssetStatus.Rejected)) { Flash("Only Draft or Rejected assets can be submitted.", true); return RedirectToAction(nameof(Details), new { id }); }
        a.Status = AssetStatus.PendingApproval; a.SubmittedAt = DateTime.UtcNow; a.RejectionReason = null;
        Assets.Log(a, "Submitted", "Submitted for Finance approval", UserName);
        await Db.SaveChangesAsync();
        Flash("Submitted for approval.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = Roles.Approvers)]
    public async Task<IActionResult> Dispose(int id)
    {
        var a = await Db.Assets.Include(x => x.Category).FirstOrDefaultAsync(x => x.Id == id);
        if (a == null) return NotFound();
        if (a.Status is not (AssetStatus.Active or AssetStatus.UnderMaintenance)) { Flash("Only active assets can be disposed.", true); return RedirectToAction(nameof(Details), new { id }); }
        return View(a);
    }

    [Authorize(Roles = Roles.Approvers), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Dispose(int id, DateTime disposalDate, decimal proceeds, string reason)
    {
        var a = await Db.Assets.FindAsync(id);
        if (a == null) return NotFound();
        if (a.Status is not (AssetStatus.Active or AssetStatus.UnderMaintenance) || string.IsNullOrWhiteSpace(reason) || proceeds < 0)
        {
            Flash("Provide a reason and non-negative proceeds for an active asset.", true);
            return RedirectToAction(nameof(Dispose), new { id });
        }
        var gainLoss = proceeds - a.BookValue;
        a.Status = AssetStatus.Disposed; a.DisposalDate = disposalDate; a.DisposalProceeds = proceeds; a.DisposalReason = reason;
        Assets.Log(a, "Disposed", $"Disposed {disposalDate:dd MMM yyyy}; proceeds {proceeds:N2}, book value {a.BookValue:N2}, {(gainLoss >= 0 ? "gain" : "loss")} {Math.Abs(gainLoss):N2}", UserName);
        await Db.SaveChangesAsync();
        Flash($"Asset disposed. {(gainLoss >= 0 ? "Gain" : "Loss")} on disposal: {Math.Abs(gainLoss):N2}");
        return RedirectToAction(nameof(Details), new { id });
    }

    private static bool IsFinanciallyEditable(Asset a) => a.Status is AssetStatus.Draft or AssetStatus.Rejected;

    private static void MapFinancials(Asset a, AssetForm f, AssetCategory cat)
    {
        a.CategoryId = f.CategoryId; a.AcquisitionCost = f.AcquisitionCost; a.AcquisitionDate = f.AcquisitionDate;
        AssetService.ApplyCategoryDefaults(a, cat);
        // Explicit values override the category defaults.
        if (f.SalvageValue != null) a.SalvageValue = f.SalvageValue.Value;
        if (f.UsefulLifeMonths != null) a.UsefulLifeMonths = f.UsefulLifeMonths.Value;
        if (f.Method != null) a.Method = f.Method.Value;
    }

    private static void MapDescriptive(Asset a, AssetForm f, bool includeOwnership = true)
    {
        a.Name = f.Name; a.Description = f.Description; a.Criticality = f.Criticality; a.Condition = f.Condition;
        a.Manufacturer = f.Manufacturer; a.ModelNumber = f.ModelNumber; a.SerialNumber = f.SerialNumber;
        a.Vendor = f.Vendor; a.PurchaseOrderNumber = f.PurchaseOrderNumber; a.Custodian = f.Custodian; a.WarrantyExpiry = f.WarrantyExpiry;
        if (includeOwnership) { a.PlantId = f.PlantId; a.LocationId = f.LocationId; a.CostCenterId = f.CostCenterId; }
    }

    private static AssetForm ToForm(Asset a) => new()
    {
        Id = a.Id, Name = a.Name, Description = a.Description, CategoryId = a.CategoryId, PlantId = a.PlantId, LocationId = a.LocationId, CostCenterId = a.CostCenterId,
        Criticality = a.Criticality, Condition = a.Condition, Manufacturer = a.Manufacturer, ModelNumber = a.ModelNumber, SerialNumber = a.SerialNumber,
        Vendor = a.Vendor, PurchaseOrderNumber = a.PurchaseOrderNumber, Custodian = a.Custodian, AcquisitionDate = a.AcquisitionDate, AcquisitionCost = a.AcquisitionCost,
        SalvageValue = a.SalvageValue, UsefulLifeMonths = a.UsefulLifeMonths, Method = a.Method, WarrantyExpiry = a.WarrantyExpiry
    };
}

public static class Csv
{
    public static string Esc(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        // Prefix formula triggers to neutralise CSV/Excel injection.
        if ("=+-@".Contains(s[0])) s = "'" + s;
        return s.Contains(',') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}

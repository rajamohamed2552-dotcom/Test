using GKM.AssetManagement.Data;
using GKM.AssetManagement.Models;
using GKM.AssetManagement.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace GKM.AssetManagement.Controllers;

/// <summary>Asset classification: categories carry the default depreciation policy and GL mapping.</summary>
public class ClassificationController : AppController
{
    public ClassificationController(AppDbContext db, AssetService assets) : base(db, assets) { }

    public async Task<IActionResult> Index()
    {
        var cats = await Db.AssetCategories.AsNoTracking()
            .Include(c => c.AssetGlAccount).Include(c => c.AccumDepGlAccount).Include(c => c.DepExpenseGlAccount)
            .OrderBy(c => c.AssetClass).ThenBy(c => c.Name).ToListAsync();
        ViewBag.Counts = await Db.Assets.GroupBy(a => a.CategoryId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
        return View(cats);
    }

    [Authorize(Roles = Roles.Approvers)]
    public async Task<IActionResult> Create()
    {
        await LoadGl();
        return View("Form", new AssetCategory());
    }

    [Authorize(Roles = Roles.Approvers), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AssetCategory c)
    {
        c.Code = (c.Code ?? "").Trim().ToUpperInvariant();
        if (await Db.AssetCategories.AnyAsync(x => x.Code == c.Code)) ModelState.AddModelError(nameof(c.Code), "Code already exists.");
        if (!ModelState.IsValid) { await LoadGl(); return View("Form", c); }
        c.Id = 0;
        Db.AssetCategories.Add(c);
        await Db.SaveChangesAsync();
        Flash($"Category {c.Code} created.");
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = Roles.Approvers)]
    public async Task<IActionResult> Edit(int id)
    {
        var c = await Db.AssetCategories.FindAsync(id);
        if (c == null) return NotFound();
        await LoadGl();
        return View("Form", c);
    }

    [Authorize(Roles = Roles.Approvers), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, AssetCategory form)
    {
        var c = await Db.AssetCategories.FindAsync(id);
        if (c == null) return NotFound();
        if (!ModelState.IsValid) { await LoadGl(); form.Id = id; return View("Form", form); }
        // Code is immutable (it is embedded in issued asset tags). Policy changes apply to new assets only.
        c.Name = form.Name; c.AssetClass = form.AssetClass; c.Description = form.Description; c.DefaultMethod = form.DefaultMethod;
        c.DefaultUsefulLifeMonths = form.DefaultUsefulLifeMonths; c.DefaultSalvagePercent = form.DefaultSalvagePercent;
        c.CapitalizationThreshold = form.CapitalizationThreshold; c.IsActive = form.IsActive;
        c.AssetGlAccountId = form.AssetGlAccountId; c.AccumDepGlAccountId = form.AccumDepGlAccountId; c.DepExpenseGlAccountId = form.DepExpenseGlAccountId;
        await Db.SaveChangesAsync();
        Flash($"Category {c.Code} updated. Changes apply to newly registered assets.");
        return RedirectToAction(nameof(Index));
    }

    private async Task LoadGl()
    {
        var all = await Db.GlAccounts.OrderBy(g => g.Code).ToListAsync();
        SelectList Pick(GlAccountType t) => new(all.Where(g => g.Type == t), "Id", "Display");
        ViewBag.AssetGl = Pick(GlAccountType.Asset);
        ViewBag.AccumGl = Pick(GlAccountType.AccumulatedDepreciation);
        ViewBag.ExpGl = Pick(GlAccountType.DepreciationExpense);
    }
}

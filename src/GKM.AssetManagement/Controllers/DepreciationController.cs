using GKM.AssetManagement.Data;
using GKM.AssetManagement.Models;
using GKM.AssetManagement.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GKM.AssetManagement.Controllers;

[Authorize(Roles = Roles.Approvers + "," + Roles.Auditor + "," + Roles.AssetAccountant)]
public class DepreciationController : AppController
{
    private readonly DepreciationService _dep;
    public DepreciationController(AppDbContext db, AssetService assets, DepreciationService dep) : base(db, assets) => _dep = dep;

    public async Task<IActionResult> Index()
    {
        var runs = await Db.DepreciationRuns.AsNoTracking().OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).ToListAsync();
        // Propose the first month after the latest run (or the current month if none).
        var next = runs.Count > 0 ? new DateTime(runs[0].Year, runs[0].Month, 1).AddMonths(1) : new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        if (next > DateTime.Today) next = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        ViewBag.Next = next;
        ViewBag.AlreadyRun = runs.Any(r => r.Year == next.Year && r.Month == next.Month);
        return View(runs);
    }

    [Authorize(Roles = Roles.Approvers), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Run(int year, int month)
    {
        try
        {
            if (month is < 1 or > 12 || year < 2000) throw new InvalidOperationException("Invalid period.");
            var run = await _dep.RunAsync(year, month, UserName);
            Flash($"Depreciation for {run.Period} posted: {run.AssetsProcessed} assets, total {run.TotalDepreciation:N2}.");
            return RedirectToAction(nameof(Details), new { run.Id });
        }
        catch (InvalidOperationException ex)
        {
            Flash(ex.Message, true);
            return RedirectToAction(nameof(Index));
        }
    }

    public async Task<IActionResult> Details(int id)
    {
        var run = await Db.DepreciationRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        if (run == null) return NotFound();
        ViewBag.Entries = await Db.DepreciationEntries.AsNoTracking().Include(e => e.Asset).ThenInclude(a => a!.Category)
            .Where(e => e.DepreciationRunId == id).OrderBy(e => e.Asset!.AssetTag).ToListAsync();
        var cc = await Db.CostCenters.AsNoTracking().ToDictionaryAsync(c => c.Id);
        ViewBag.CostCenters = cc;
        return View(run);
    }
}

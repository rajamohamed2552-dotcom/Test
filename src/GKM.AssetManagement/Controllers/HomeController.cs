using System.Diagnostics;
using GKM.AssetManagement.Data;
using GKM.AssetManagement.Models;
using GKM.AssetManagement.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GKM.AssetManagement.Controllers;

public class HomeController : Controller
{
    private readonly AppDbContext _db;
    public HomeController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var live = new[] { AssetStatus.Active, AssetStatus.UnderMaintenance };
        // Light projections, aggregated in memory (keeps the code portable across Postgres and SQLite).
        var assets = await _db.Assets.AsNoTracking()
            .Select(a => new { a.Status, a.AcquisitionCost, a.AccumulatedDepreciation, Class = a.Category!.AssetClass, Plant = a.Plant!.Code })
            .ToListAsync();
        var liveAssets = assets.Where(a => live.Contains(a.Status)).ToList();

        var yearStart = new DateTime(DateTime.Today.Year, 1, 1);
        var sixMonthsAgo = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-5);
        var wos = await _db.WorkOrders.AsNoTracking().Where(w => w.Status == WorkOrderStatus.Completed && w.CompletedOn >= sixMonthsAgo).ToListAsync();
        var ytdCost = await _db.WorkOrders.AsNoTracking().Where(w => w.Status == WorkOrderStatus.Completed && w.CompletedOn >= yearStart)
            .Select(w => new { w.LaborCost, w.PartsCost, w.ExternalCost }).ToListAsync();
        var dep = await _db.DepreciationRuns.AsNoTracking().OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).Take(6).ToListAsync();

        var vm = new DashboardViewModel
        {
            TotalAssets = liveAssets.Count,
            GrossCost = liveAssets.Sum(a => a.AcquisitionCost),
            AccumulatedDepreciation = liveAssets.Sum(a => a.AccumulatedDepreciation),
            PendingApprovals = assets.Count(a => a.Status == AssetStatus.PendingApproval),
            UnderMaintenance = assets.Count(a => a.Status == AssetStatus.UnderMaintenance),
            OpenWorkOrders = await _db.WorkOrders.CountAsync(w => w.Status == WorkOrderStatus.Open || w.Status == WorkOrderStatus.InProgress),
            OverduePlans = await _db.MaintenancePlans.CountAsync(p => p.IsActive && p.NextDueDate < DateTime.Today),
            MaintenanceCostYtd = ytdCost.Sum(w => w.LaborCost + w.PartsCost + w.ExternalCost),
            NbvByClass = liveAssets.GroupBy(a => a.Class).Select(g => (g.Key, g.Sum(a => a.AcquisitionCost - a.AccumulatedDepreciation))).OrderByDescending(x => x.Item2).ToList(),
            CountByPlant = liveAssets.GroupBy(a => a.Plant).Select(g => (g.Key, (decimal)g.Count())).ToList(),
            CountByStatus = assets.GroupBy(a => a.Status).Select(g => (g.Key.ToString(), (decimal)g.Count())).ToList(),
            MaintenanceByMonth = Enumerable.Range(0, 6).Select(i => sixMonthsAgo.AddMonths(i))
                .Select(m => (m.ToString("MMM yy"), wos.Where(w => w.CompletedOn!.Value.Year == m.Year && w.CompletedOn.Value.Month == m.Month).Sum(w => w.LaborCost + w.PartsCost + w.ExternalCost))).ToList(),
            DepreciationByMonth = dep.OrderBy(r => r.Year).ThenBy(r => r.Month).Select(r => (r.Period, r.TotalDepreciation)).ToList(),
            RecentEvents = await _db.AssetEvents.AsNoTracking().Include(e => e.Asset).OrderByDescending(e => e.At).Take(8).ToListAsync(),
        };
        vm.NetBookValue = vm.GrossCost - vm.AccumulatedDepreciation;
        return View(vm);
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}

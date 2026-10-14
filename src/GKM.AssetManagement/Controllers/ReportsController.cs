using System.Text;
using GKM.AssetManagement.Data;
using GKM.AssetManagement.Models;
using GKM.AssetManagement.Services;
using GKM.AssetManagement.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GKM.AssetManagement.Controllers;

[Authorize(Roles = Roles.Admin + "," + Roles.FinanceManager + "," + Roles.AssetAccountant + "," + Roles.Auditor)]
public class ReportsController : AppController
{
    public ReportsController(AppDbContext db, AssetService assets) : base(db, assets) { }

    public IActionResult Index() => View();

    public async Task<IActionResult> CostCenters(bool csv = false)
    {
        var live = new[] { AssetStatus.Active, AssetStatus.UnderMaintenance };
        var assets = await Db.Assets.AsNoTracking().Where(a => live.Contains(a.Status))
            .Select(a => new { a.CostCenterId, a.AcquisitionCost, a.AccumulatedDepreciation }).ToListAsync();
        var maint = (await Db.WorkOrders.AsNoTracking().Where(w => w.Status == WorkOrderStatus.Completed)
            .Select(w => new { w.Asset!.CostCenterId, w.LaborCost, w.PartsCost, w.ExternalCost }).ToListAsync())
            .GroupBy(w => w.CostCenterId).ToDictionary(g => g.Key, g => g.Sum(w => w.LaborCost + w.PartsCost + w.ExternalCost));
        var ccs = await Db.CostCenters.AsNoTracking().Include(c => c.Plant).OrderBy(c => c.Code).ToListAsync();
        var rows = ccs.Select(c =>
        {
            var mine = assets.Where(a => a.CostCenterId == c.Id).ToList();
            return new CostCenterSummaryRow
            {
                CostCenter = c.Display, Plant = c.Plant?.Code ?? "Corporate", AssetCount = mine.Count, Cost = mine.Sum(a => a.AcquisitionCost),
                AccumulatedDepreciation = mine.Sum(a => a.AccumulatedDepreciation), MaintenanceCost = maint.GetValueOrDefault(c.Id)
            };
        }).Where(r => r.AssetCount > 0 || r.MaintenanceCost > 0).ToList();

        if (csv)
        {
            var sb = new StringBuilder("CostCenter,Plant,Assets,Cost,AccumulatedDepreciation,NetBookValue,MaintenanceCost\n");
            foreach (var r in rows) sb.AppendLine(string.Join(',', Csv.Esc(r.CostCenter), r.Plant, r.AssetCount, r.Cost, r.AccumulatedDepreciation, r.NetBookValue, r.MaintenanceCost));
            return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "cost-center-summary.csv");
        }
        return View(rows);
    }

    /// <summary>Total cost of ownership: acquisition + lifetime maintenance spend, highest maintenance ratio first.</summary>
    public async Task<IActionResult> Tco(bool csv = false)
    {
        var maint = (await Db.WorkOrders.AsNoTracking().Where(w => w.Status == WorkOrderStatus.Completed).ToListAsync())
            .GroupBy(w => w.AssetId).ToDictionary(g => g.Key, g => (Cost: g.Sum(w => w.TotalCost), Count: g.Count()));
        var assets = await Db.Assets.AsNoTracking().Include(a => a.Category).Include(a => a.Plant).Where(a => a.Status != AssetStatus.Draft && a.Status != AssetStatus.Rejected && a.Status != AssetStatus.PendingApproval).ToListAsync();
        var rows = assets.Select(a =>
        {
            var m = maint.GetValueOrDefault(a.Id);
            return (Asset: a, Maintenance: m.Cost, Orders: m.Count, Ratio: a.AcquisitionCost == 0 ? 0 : m.Cost / a.AcquisitionCost * 100m);
        }).OrderByDescending(r => r.Ratio).ToList();

        if (csv)
        {
            var sb = new StringBuilder("AssetTag,Name,Category,Plant,AcquisitionCost,BookValue,MaintenanceCost,WorkOrders,MaintenancePctOfCost\n");
            foreach (var r in rows) sb.AppendLine(string.Join(',', Csv.Esc(r.Asset.AssetTag), Csv.Esc(r.Asset.Name), Csv.Esc(r.Asset.Category?.Name), r.Asset.Plant?.Code, r.Asset.AcquisitionCost, r.Asset.BookValue, r.Maintenance, r.Orders, Math.Round(r.Ratio, 2)));
            return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "tco.csv");
        }
        return View(rows.Take(100).ToList());
    }

    /// <summary>Depreciation journal for a period: Dr depreciation expense / Cr accumulated depreciation, per cost centre.</summary>
    public async Task<IActionResult> Journal(int? year, int? month, bool csv = false)
    {
        var last = await Db.DepreciationRuns.OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).FirstOrDefaultAsync();
        year ??= last?.Year ?? DateTime.Today.Year; month ??= last?.Month ?? DateTime.Today.Month;
        var entries = await Db.DepreciationEntries.AsNoTracking().Include(e => e.Asset).ThenInclude(a => a!.Category).ThenInclude(c => c!.AccumDepGlAccount)
            .Where(e => e.Year == year && e.Month == month).ToListAsync();
        var gls = await Db.GlAccounts.AsNoTracking().ToDictionaryAsync(g => g.Id);
        var ccs = await Db.CostCenters.AsNoTracking().ToDictionaryAsync(c => c.Id);
        var rows = entries
            .GroupBy(e => (Exp: e.DepExpenseGlAccountId, Acc: e.Asset!.Category!.AccumDepGlAccountId, e.CostCenterId))
            .Select(g => new GlJournalRow
            {
                DebitAccount = g.Key.Exp != null ? gls[g.Key.Exp.Value].Display : "(unmapped)",
                CreditAccount = g.Key.Acc != null ? gls[g.Key.Acc.Value].Display : "(unmapped)",
                CostCenter = ccs[g.Key.CostCenterId].Display, Description = $"Depreciation {new DateTime(year.Value, month.Value, 1):MMM yyyy}",
                Amount = g.Sum(e => e.Amount)
            }).OrderBy(r => r.DebitAccount).ThenBy(r => r.CostCenter).ToList();

        if (csv)
        {
            var sb = new StringBuilder("Period,DebitAccount,CreditAccount,CostCenter,Amount\n");
            foreach (var r in rows) sb.AppendLine(string.Join(',', $"{year}-{month:D2}", Csv.Esc(r.DebitAccount), Csv.Esc(r.CreditAccount), Csv.Esc(r.CostCenter), r.Amount));
            return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"depreciation-journal-{year}-{month:D2}.csv");
        }
        ViewBag.Year = year; ViewBag.Month = month;
        ViewBag.Periods = await Db.DepreciationRuns.AsNoTracking().OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).ToListAsync();
        return View(rows);
    }
}

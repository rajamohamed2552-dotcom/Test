using GKM.AssetManagement.Data;
using GKM.AssetManagement.Models;
using GKM.AssetManagement.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GKM.AssetManagement.Controllers;

/// <summary>Maker-checker approval queue for newly registered assets.</summary>
[Authorize(Roles = Roles.Approvers)]
public class OnboardingController : AppController
{
    public OnboardingController(AppDbContext db, AssetService assets) : base(db, assets) { }

    public async Task<IActionResult> Index() =>
        View(await Db.Assets.AsNoTracking().Include(a => a.Category).Include(a => a.Plant).Include(a => a.CostCenter)
            .Where(a => a.Status == AssetStatus.PendingApproval).OrderBy(a => a.SubmittedAt).ToListAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, DateTime? capitalizationDate)
    {
        var a = await Db.Assets.FindAsync(id);
        if (a == null || a.Status != AssetStatus.PendingApproval) return NotFound();

        // Segregation of duties: the person who registered the asset cannot approve it.
        if (string.Equals(a.CreatedBy, UserName, StringComparison.OrdinalIgnoreCase))
        {
            Flash("You cannot approve an asset you registered (segregation of duties).", true);
            return RedirectToAction(nameof(Index));
        }
        var capDate = (capitalizationDate ?? a.AcquisitionDate).Date;
        if (capDate < a.AcquisitionDate.Date || capDate > DateTime.Today)
        {
            Flash("Capitalisation date must be between the acquisition date and today.", true);
            return RedirectToAction(nameof(Index));
        }
        a.Status = AssetStatus.Active; a.CapitalizationDate = capDate; a.ApprovedBy = UserName; a.ApprovedAt = DateTime.UtcNow; a.RejectionReason = null;
        Assets.Log(a, "Approved", $"Approved and capitalised effective {capDate:dd MMM yyyy}", UserName);
        await Db.SaveChangesAsync();
        Flash($"{a.AssetTag} approved and capitalised.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string reason)
    {
        var a = await Db.Assets.FindAsync(id);
        if (a == null || a.Status != AssetStatus.PendingApproval) return NotFound();
        if (string.IsNullOrWhiteSpace(reason)) { Flash("A rejection reason is required.", true); return RedirectToAction(nameof(Index)); }
        a.Status = AssetStatus.Rejected; a.RejectionReason = reason.Trim();
        Assets.Log(a, "Rejected", $"Rejected: {a.RejectionReason}", UserName);
        await Db.SaveChangesAsync();
        Flash($"{a.AssetTag} rejected and returned to the requester.");
        return RedirectToAction(nameof(Index));
    }
}

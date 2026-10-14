using GKM.AssetManagement.Data;
using GKM.AssetManagement.Models;
using GKM.AssetManagement.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GKM.AssetManagement.Controllers;

/// <summary>Finance master data: plants, locations, cost centres and GL accounts.</summary>
[Authorize(Roles = Roles.Approvers)]
public class MastersController : AppController
{
    public MastersController(AppDbContext db, AssetService assets) : base(db, assets) { }

    public async Task<IActionResult> Index()
    {
        ViewBag.Plants = await Db.Plants.AsNoTracking().OrderBy(p => p.Code).ToListAsync();
        ViewBag.Locations = await Db.Locations.AsNoTracking().Include(l => l.Plant).OrderBy(l => l.Plant!.Code).ThenBy(l => l.Name).ToListAsync();
        ViewBag.CostCenters = await Db.CostCenters.AsNoTracking().Include(c => c.Plant).OrderBy(c => c.Code).ToListAsync();
        ViewBag.GlAccounts = await Db.GlAccounts.AsNoTracking().OrderBy(g => g.Code).ToListAsync();
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPlant(string code, string name, string? city)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        if (code.Length is 0 or > 10 || string.IsNullOrWhiteSpace(name)) return Fail("Plant code (max 10) and name are required.");
        if (await Db.Plants.AnyAsync(p => p.Code == code)) return Fail($"Plant {code} already exists.");
        Db.Plants.Add(new Plant { Code = code, Name = name.Trim(), City = city?.Trim() });
        await Db.SaveChangesAsync();
        return Ok2($"Plant {code} added.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddLocation(int plantId, string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name) || !await Db.Plants.AnyAsync(p => p.Id == plantId)) return Fail("Plant and location name are required.");
        Db.Locations.Add(new Location { PlantId = plantId, Name = name.Trim(), Description = description?.Trim() });
        await Db.SaveChangesAsync();
        return Ok2("Location added.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCostCenter(string code, string name, int? plantId, string? owner)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        if (code.Length is 0 or > 20 || string.IsNullOrWhiteSpace(name)) return Fail("Cost centre code (max 20) and name are required.");
        if (await Db.CostCenters.AnyAsync(c => c.Code == code)) return Fail($"Cost centre {code} already exists.");
        Db.CostCenters.Add(new CostCenter { Code = code, Name = name.Trim(), PlantId = plantId, Owner = owner?.Trim() });
        await Db.SaveChangesAsync();
        return Ok2($"Cost centre {code} added.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddGlAccount(string code, string name, GlAccountType type)
    {
        code = (code ?? "").Trim();
        if (code.Length is 0 or > 20 || string.IsNullOrWhiteSpace(name)) return Fail("GL code (max 20) and name are required.");
        if (await Db.GlAccounts.AnyAsync(g => g.Code == code)) return Fail($"GL account {code} already exists.");
        Db.GlAccounts.Add(new GlAccount { Code = code, Name = name.Trim(), Type = type });
        await Db.SaveChangesAsync();
        return Ok2($"GL account {code} added.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(string kind, int id)
    {
        // Master records are deactivated, never deleted, so historical postings keep their references.
        switch (kind)
        {
            case "plant": { var x = await Db.Plants.FindAsync(id); if (x != null) x.IsActive = !x.IsActive; break; }
            case "location": { var x = await Db.Locations.FindAsync(id); if (x != null) x.IsActive = !x.IsActive; break; }
            case "costcenter": { var x = await Db.CostCenters.FindAsync(id); if (x != null) x.IsActive = !x.IsActive; break; }
            default: return BadRequest();
        }
        await Db.SaveChangesAsync();
        return Ok2("Status updated.");
    }

    private IActionResult Fail(string m) { Flash(m, true); return RedirectToAction(nameof(Index)); }
    private IActionResult Ok2(string m) { Flash(m); return RedirectToAction(nameof(Index)); }
}

using GKM.AssetManagement.Data;
using GKM.AssetManagement.Models;
using GKM.AssetManagement.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace GKM.AssetManagement.Controllers;

/// <summary>Base class with shared lookup/dropdown helpers.</summary>
public abstract class AppController : Controller
{
    protected readonly AppDbContext Db;
    protected readonly AssetService Assets;

    protected AppController(AppDbContext db, AssetService assets)
    {
        Db = db;
        Assets = assets;
    }

    protected string UserName => User.Identity?.Name ?? "system";

    protected async Task LoadAssetLookupsAsync()
    {
        ViewBag.Categories = new SelectList(await Db.AssetCategories.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync(), "Id", "Name");
        ViewBag.Plants = new SelectList(await Db.Plants.Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync(), "Id", "Name");
        ViewBag.CostCenters = new SelectList(await Db.CostCenters.Where(c => c.IsActive).OrderBy(c => c.Code).ToListAsync(), "Id", "Display");
        // Locations are filtered client-side by plant, so the plant id is rendered into each option.
        ViewBag.Locations = await Db.Locations.Include(l => l.Plant).Where(l => l.IsActive).OrderBy(l => l.PlantId).ThenBy(l => l.Name).ToListAsync();
    }

    protected void Flash(string message, bool error = false) => TempData[error ? "Error" : "Success"] = message;
}

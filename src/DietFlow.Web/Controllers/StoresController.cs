using DietFlow.Web.Data;
using DietFlow.Web.Models.Entities;
using DietFlow.Web.Models.ViewModels;
using DietFlow.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace DietFlow.Web.Controllers;

public class StoresController(AppDbContext db, CityContext cityContext, CacheInvalidator cache) : Controller
{
    public async Task<IActionResult> Index(string? chain)
    {
        var city = await cityContext.GetCurrentAsync();
        var cityId = city?.Id ?? 0;

        var query = db.Stores.AsNoTracking().Where(s => s.CityId == cityId);
        if (!string.IsNullOrWhiteSpace(chain))
            query = query.Where(s => s.Chain == chain);

        var stores = await query
            .OrderBy(s => s.Chain).ThenBy(s => s.Name)
            .Select(s => new StoreListItemVm(s.Id, s.Name, s.Chain, s.Address, s.Prices.Count))
            .ToListAsync();

        ViewBag.Chains = await db.Stores.AsNoTracking().Where(s => s.CityId == cityId)
            .Select(s => s.Chain).Distinct().OrderBy(c => c).ToListAsync();
        ViewBag.Chain = chain;
        ViewBag.CityName = city?.Name;
        return View(stores);
    }

    public async Task<IActionResult> Details(int id)
    {
        var store = await db.Stores.AsNoTracking()
            .Include(s => s.City)
            .Include(s => s.Prices.OrderBy(p => p.Product.Name)).ThenInclude(p => p.Product)
            .FirstOrDefaultAsync(s => s.Id == id);
        return store is null ? NotFound() : View(store);
    }

    public async Task<IActionResult> Create()
    {
        var city = await cityContext.GetCurrentAsync();
        await FillLookups();
        return View(new StoreFormVm { CityId = city?.Id ?? 0, Latitude = city?.Latitude ?? 0, Longitude = city?.Longitude ?? 0 });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(StoreFormVm vm)
    {
        await ValidateCity(vm);
        if (!ModelState.IsValid)
        {
            await FillLookups();
            return View(vm);
        }

        var store = new Store();
        Apply(vm, store);
        db.Stores.Add(store);
        await db.SaveChangesAsync();
        await cache.StoreChangedAsync();

        TempData["Success"] = $"Магазин «{store.Name}» додано";
        return RedirectToAction(nameof(Details), new { id = store.Id });
    }

    public async Task<IActionResult> Edit(int id)
    {
        var s = await db.Stores.FindAsync(id);
        if (s is null) return NotFound();

        await FillLookups();
        return View(new StoreFormVm
        {
            Id = s.Id, Name = s.Name, Chain = s.Chain, Address = s.Address,
            Latitude = s.Latitude, Longitude = s.Longitude, CityId = s.CityId,
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, StoreFormVm vm)
    {
        if (id != vm.Id) return BadRequest();
        await ValidateCity(vm);
        if (!ModelState.IsValid)
        {
            await FillLookups();
            return View(vm);
        }

        var store = await db.Stores.FindAsync(id);
        if (store is null) return NotFound();

        Apply(vm, store);
        await db.SaveChangesAsync();
        await cache.StoreChangedAsync();

        TempData["Success"] = "Зміни збережено";
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Delete(int id)
    {
        var store = await db.Stores.AsNoTracking().Include(s => s.City).FirstOrDefaultAsync(s => s.Id == id);
        if (store is null) return NotFound();

        ViewBag.PriceCount = await db.StorePrices.CountAsync(sp => sp.StoreId == id);
        return View(store);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var store = await db.Stores.FindAsync(id);
        if (store is null) return NotFound();

        db.Stores.Remove(store); 
        await db.SaveChangesAsync();
        await cache.StoreChangedAsync();

        TempData["Success"] = $"Магазин «{store.Name}» видалено";
        return RedirectToAction(nameof(Index));
    }

    private async Task ValidateCity(StoreFormVm vm)
    {
        if (!await db.Cities.AnyAsync(c => c.Id == vm.CityId))
            ModelState.AddModelError(nameof(vm.CityId), "Оберіть місто зі списку");
    }

    private async Task FillLookups()
    {
        ViewBag.Cities = new SelectList(await db.Cities.AsNoTracking().OrderBy(c => c.Name).ToListAsync(), "Id", "Name");
        var dbChains = await db.Stores.AsNoTracking().Select(s => s.Chain).Distinct().ToListAsync();
        ViewBag.Chains = DbSeeder.KnownChains.Union(dbChains).OrderBy(c => c).ToList();
    }

    private static void Apply(StoreFormVm vm, Store s)
    {
        s.Name = vm.Name.Trim();
        s.Chain = vm.Chain.Trim();
        s.Address = string.IsNullOrWhiteSpace(vm.Address) ? null : vm.Address.Trim();
        s.Latitude = vm.Latitude;
        s.Longitude = vm.Longitude;
        s.CityId = vm.CityId;
    }
}

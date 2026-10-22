using DietFlow.Web.Data;
using DietFlow.Web.Models.Entities;
using DietFlow.Web.Models.ViewModels;
using DietFlow.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace DietFlow.Web.Controllers;

[Route("Stores/{storeId:int}/Prices")]
public class StorePricesController(AppDbContext db, CacheInvalidator cache) : Controller
{
    [HttpGet("Create")]
    public async Task<IActionResult> Create(int storeId)
    {
        var store = await db.Stores.FindAsync(storeId);
        if (store is null) return NotFound();

        return View(new StorePriceFormVm { StoreId = storeId, StoreName = store.Name });
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int storeId, StorePriceFormVm vm)
    {
        var store = await db.Stores.FindAsync(storeId);
        if (store is null) return NotFound();
        vm.StoreId = storeId; 

        if (!await db.Products.AnyAsync(p => p.Id == vm.ProductId))
            ModelState.AddModelError(nameof(vm.ProductId), "Оберіть продукт зі списку");
        else if (await db.StorePrices.AnyAsync(sp => sp.StoreId == storeId && sp.ProductId == vm.ProductId))
            ModelState.AddModelError(nameof(vm.ProductId), "Ціна на цей продукт у магазині вже є — відредагуйте її");

        if (!ModelState.IsValid)
        {
            vm.StoreName = store.Name;
            vm.ProductName = await db.Products.Where(p => p.Id == vm.ProductId).Select(p => p.Name).FirstOrDefaultAsync();
            return View(vm);
        }

        db.StorePrices.Add(new StorePrice { StoreId = storeId, ProductId = vm.ProductId, PricePerKg = vm.PricePerKg });
        await db.SaveChangesAsync();
        await cache.PriceChangedAsync();

        TempData["Success"] = "Ціну додано";
        return RedirectToAction("Details", "Stores", new { id = storeId });
    }

    [HttpGet("{productId:int}/Edit")]
    public async Task<IActionResult> Edit(int storeId, int productId)
    {
        var sp = await Find(storeId, productId);
        if (sp is null) return NotFound();

        return View(new StorePriceFormVm
        {
            StoreId = storeId, ProductId = productId, PricePerKg = sp.PricePerKg,
            StoreName = sp.Store.Name, ProductName = sp.Product.Name,
        });
    }

    [HttpPost("{productId:int}/Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int storeId, int productId, StorePriceFormVm vm)
    {
        var sp = await Find(storeId, productId);
        if (sp is null) return NotFound();

        ModelState.Remove(nameof(vm.ProductId));
        if (!ModelState.IsValid)
        {
            vm.StoreId = storeId; vm.ProductId = productId;
            vm.StoreName = sp.Store.Name; vm.ProductName = sp.Product.Name;
            return View(vm);
        }

        sp.PricePerKg = vm.PricePerKg;
        sp.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await cache.PriceChangedAsync();

        TempData["Success"] = $"Ціну на «{sp.Product.Name}» оновлено";
        return RedirectToAction("Details", "Stores", new { id = storeId });
    }

    [HttpGet("{productId:int}/Delete")]
    public async Task<IActionResult> Delete(int storeId, int productId)
    {
        var sp = await Find(storeId, productId);
        return sp is null ? NotFound() : View(sp);
    }

    [HttpPost("{productId:int}/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int storeId, int productId)
    {
        var sp = await Find(storeId, productId);
        if (sp is null) return NotFound();

        db.StorePrices.Remove(sp);
        await db.SaveChangesAsync();
        await cache.PriceChangedAsync();

        TempData["Success"] = $"Ціну на «{sp.Product.Name}» видалено";
        return RedirectToAction("Details", "Stores", new { id = storeId });
    }

    private Task<StorePrice?> Find(int storeId, int productId) =>
        db.StorePrices.Include(sp => sp.Store).Include(sp => sp.Product)
            .FirstOrDefaultAsync(sp => sp.StoreId == storeId && sp.ProductId == productId);
    
}

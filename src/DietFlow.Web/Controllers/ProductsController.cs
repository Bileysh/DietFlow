using DietFlow.Web.Data;
using DietFlow.Web.Models.Entities;
using DietFlow.Web.Models.ViewModels;
using DietFlow.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DietFlow.Web.Controllers;

public class ProductsController(AppDbContext db, CityContext cityContext) : Controller
{
    private const int PageSize = 20;

    public async Task<IActionResult> Index(string? q, string? category, string sort = "name", int page = 1)
    {
        var city = await cityContext.GetCurrentAsync();
        var cityId = city?.Id ?? 0;

        var query = db.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(p => EF.Functions.ILike(p.Name, $"%{q.Trim()}%"));

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(p => p.Category == category);

        query = sort switch
        {
            "kcal" => query.OrderBy(p => p.KcalPer100g),
            "kcal_desc" => query.OrderByDescending(p => p.KcalPer100g),
            "protein_desc" => query.OrderByDescending(p => p.ProteinPer100g),
            _ => query.OrderBy(p => p.Name),
        };

        var total = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        page = Math.Clamp(page, 1, totalPages);

        var items = await query
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(p => new ProductListItemVm(
                p.Id, p.Name, p.Category,
                p.KcalPer100g, p.ProteinPer100g, p.FatPer100g, p.CarbsPer100g,
                p.Prices.Where(sp => sp.Store.CityId == cityId).Min(sp => (decimal?)sp.PricePerKg),
                p.Prices.Count(sp => sp.Store.CityId == cityId)))
            .ToListAsync();

        var categories = await db.Products.AsNoTracking()
            .Where(p => p.Category != null).Select(p => p.Category!)
            .Distinct().OrderBy(c => c).ToListAsync();

        return View(new ProductIndexVm
        {
            Items = items, Categories = categories, Q = q, Category = category, Sort = sort,
            Page = page, TotalPages = totalPages, TotalCount = total, CityName = city?.Name,
        });
    }

    public async Task<IActionResult> Details(int id)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (product is null) return NotFound();

        var city = await cityContext.GetCurrentAsync();
        var cityId = city?.Id ?? 0;

        var prices = await db.StorePrices.AsNoTracking()
            .Where(sp => sp.ProductId == id && sp.Store.CityId == cityId)
            .OrderBy(sp => sp.PricePerKg)
            .Select(sp => new ProductPriceRowVm(sp.StoreId, sp.Store.Name, sp.Store.Chain, sp.Store.Address, sp.PricePerKg))
            .ToListAsync();

        var recipes = await db.RecipeIngredients.AsNoTracking()
            .Where(ri => ri.ProductId == id).Select(ri => ri.Recipe.Title).OrderBy(t => t).ToListAsync();

        return View(new ProductDetailsVm { Product = product, CityName = city?.Name, Prices = prices, UsedInRecipes = recipes });
    }

    public IActionResult Create() => View(new ProductFormVm());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProductFormVm vm)
    {
        await ValidateBarcodeUnique(vm);
        if (!ModelState.IsValid) return View(vm);

        var product = new Product();
        Apply(vm, product);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        TempData["Success"] = $"Продукт «{product.Name}» додано";
        return RedirectToAction(nameof(Details), new { id = product.Id });
    }

    public async Task<IActionResult> Edit(int id)
    {
        var p = await db.Products.FindAsync(id);
        if (p is null) return NotFound();

        return View(new ProductFormVm
        {
            Id = p.Id, Name = p.Name, Category = p.Category, Barcode = p.Barcode,
            KcalPer100g = p.KcalPer100g, ProteinPer100g = p.ProteinPer100g,
            FatPer100g = p.FatPer100g, CarbsPer100g = p.CarbsPer100g,
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ProductFormVm vm)
    {
        if (id != vm.Id) return BadRequest();
        await ValidateBarcodeUnique(vm);
        if (!ModelState.IsValid) return View(vm);

        var product = await db.Products.FindAsync(id);
        if (product is null) return NotFound();

        Apply(vm, product);
        await db.SaveChangesAsync();

        TempData["Success"] = "Зміни збережено";
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Delete(int id)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (product is null) return NotFound();

        ViewBag.UsedInRecipes = await RecipesUsing(id);
        return View(product);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var product = await db.Products.FindAsync(id);
        if (product is null) return NotFound();
        
        var usedIn = await RecipesUsing(id);
        if (usedIn.Count > 0)
        {
            ViewBag.UsedInRecipes = usedIn;
            ModelState.AddModelError("", "Продукт не можна видалити, поки він використовується в рецептах.");
            return View(product);
        }

        db.Products.Remove(product); 
        await db.SaveChangesAsync();

        TempData["Success"] = $"Продукт «{product.Name}» видалено";
        return RedirectToAction(nameof(Index));
    }

    private async Task ValidateBarcodeUnique(ProductFormVm vm)
    {
        if (string.IsNullOrWhiteSpace(vm.Barcode)) return;
        var barcode = vm.Barcode.Trim();
        if (await db.Products.AnyAsync(p => p.Barcode == barcode && p.Id != vm.Id))
            ModelState.AddModelError(nameof(vm.Barcode), "Продукт з таким штрихкодом уже існує");
    }

    private Task<List<string>> RecipesUsing(int productId) =>
        db.RecipeIngredients.Where(ri => ri.ProductId == productId)
            .Select(ri => ri.Recipe.Title).OrderBy(t => t).ToListAsync();

    private static void Apply(ProductFormVm vm, Product p)
    {
        p.Name = vm.Name.Trim();
        p.Category = string.IsNullOrWhiteSpace(vm.Category) ? null : vm.Category.Trim();
        p.Barcode = string.IsNullOrWhiteSpace(vm.Barcode) ? null : vm.Barcode.Trim();
        p.KcalPer100g = vm.KcalPer100g;
        p.ProteinPer100g = vm.ProteinPer100g;
        p.FatPer100g = vm.FatPer100g;
        p.CarbsPer100g = vm.CarbsPer100g;
    }
}

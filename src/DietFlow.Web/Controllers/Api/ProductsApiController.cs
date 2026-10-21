using DietFlow.Web.Data;
using DietFlow.Web.Models.Api;
using DietFlow.Web.Models.Entities;
using DietFlow.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;

namespace DietFlow.Web.Controllers.Api;

[Route("api/v1/products")]
[Tags("Products")]
public class ProductsApiController(AppDbContext db, CacheInvalidator cache) : ApiControllerBase
{
    [HttpGet]
    [OutputCache(PolicyName = CacheTags.Policy, Tags = [CacheTags.Products])]
    public async Task<PagedResult<ProductDto>> List(
        string? q, string? category, int? cityId, string sort = "name", int? skip = null, int? limit = null)
    {
        var (s, l) = Normalize(skip, limit);
        var query = db.Products.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(p => EF.Functions.ILike(p.Name, $"%{q.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(p => p.Category == category);

        query = sort switch
        {
            "kcal" => query.OrderBy(p => p.KcalPer100g).ThenBy(p => p.Id),
            "-kcal" => query.OrderByDescending(p => p.KcalPer100g).ThenBy(p => p.Id),
            "-protein" => query.OrderByDescending(p => p.ProteinPer100g).ThenBy(p => p.Id),
            _ => query.OrderBy(p => p.Name).ThenBy(p => p.Id),
        };

        var total = await query.CountAsync();
        var items = await query.Skip(s).Take(l).Select(ToDto(cityId)).ToListAsync();
        return Page(items, total, s, l);
    }

    [HttpGet("{id:int}")]
    [OutputCache(PolicyName = CacheTags.Policy, Tags = [CacheTags.Products])]
    public async Task<ActionResult<ProductDetailsDto>> Get(int id, int? cityId)
    {
        var product = await db.Products.AsNoTracking().Where(p => p.Id == id).Select(ToDto(cityId)).FirstOrDefaultAsync();
        if (product is null) return NotFound();

        var prices = await db.StorePrices.AsNoTracking()
            .Where(sp => sp.ProductId == id && (cityId == null || sp.Store.CityId == cityId))
            .OrderBy(sp => sp.PricePerKg)
            .Select(sp => new ProductPriceDto(sp.StoreId, sp.Store.Name, sp.Store.Chain, sp.Store.CityId, sp.PricePerKg, sp.UpdatedAt))
            .ToListAsync();

        return new ProductDetailsDto(product, prices, await DomainRules.RecipesUsingProductAsync(db, id));
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductDto>> Create(ProductRequest request)
    {
        if (await DomainRules.BarcodeTakenAsync(db, request.Barcode, 0))
            return ConflictProblem("A product with this barcode already exists.");

        var product = new Product();
        Apply(request, product);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        await cache.ProductChangedAsync();

        return CreatedAtAction(nameof(Get), new { id = product.Id }, Map(product));
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductDto>> Update(int id, ProductRequest request)
    {
        var product = await db.Products.FindAsync(id);
        if (product is null) return NotFound();
        if (await DomainRules.BarcodeTakenAsync(db, request.Barcode, id))
            return ConflictProblem("A product with this barcode already exists.");

        Apply(request, product);
        await db.SaveChangesAsync();
        await cache.ProductChangedAsync();
        return Map(product);
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id)
    {
        var product = await db.Products.FindAsync(id);
        if (product is null) return NotFound();

        var usedIn = await DomainRules.RecipesUsingProductAsync(db, id);
        if (usedIn.Count > 0)
            return ConflictProblem($"Product is used in recipes: {string.Join(", ", usedIn)}.");

        db.Products.Remove(product);
        await db.SaveChangesAsync();
        await cache.ProductChangedAsync();
        return NoContent();
    }

    private static System.Linq.Expressions.Expression<Func<Product, ProductDto>> ToDto(int? cityId) =>
        p => new ProductDto(p.Id, p.Name, p.Category, p.KcalPer100g, p.ProteinPer100g, p.FatPer100g, p.CarbsPer100g, p.Barcode,
            p.Prices.Where(sp => cityId == null || sp.Store.CityId == cityId).Min(sp => (decimal?)sp.PricePerKg));

    private static ProductDto Map(Product p) =>
        new(p.Id, p.Name, p.Category, p.KcalPer100g, p.ProteinPer100g, p.FatPer100g, p.CarbsPer100g, p.Barcode, null);

    private static void Apply(ProductRequest r, Product p)
    {
        p.Name = r.Name.Trim();
        p.Category = string.IsNullOrWhiteSpace(r.Category) ? null : r.Category.Trim();
        p.Barcode = string.IsNullOrWhiteSpace(r.Barcode) ? null : r.Barcode.Trim();
        p.KcalPer100g = r.KcalPer100g;
        p.ProteinPer100g = r.ProteinPer100g;
        p.FatPer100g = r.FatPer100g;
        p.CarbsPer100g = r.CarbsPer100g;
    }
}

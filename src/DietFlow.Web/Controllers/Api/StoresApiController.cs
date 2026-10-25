using DietFlow.Web.Data;
using DietFlow.Web.Models.Api;
using DietFlow.Web.Models.Entities;
using DietFlow.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;

namespace DietFlow.Web.Controllers.Api;

[Route("api/v1/stores")]
[Tags("Stores & prices")]
public class StoresApiController(AppDbContext db, CacheInvalidator cache) : ApiControllerBase
{
    [HttpGet]
    [OutputCache(PolicyName = CacheTags.Policy, Tags = [CacheTags.Stores])]
    public async Task<PagedResult<StoreDto>> List(int? cityId, string? chain, int? skip = null, int? limit = null)
    {
        var (s, l) = Normalize(skip, limit);
        var query = db.Stores.AsNoTracking();
        if (cityId is not null) query = query.Where(st => st.CityId == cityId);
        if (!string.IsNullOrWhiteSpace(chain)) query = query.Where(st => st.Chain == chain);

        var total = await query.CountAsync();
        var items = await query.OrderBy(st => st.CityId).ThenBy(st => st.Chain).ThenBy(st => st.Name).ThenBy(st => st.Id)
            .Skip(s).Take(l)
            .Select(st => new StoreDto(st.Id, st.Name, st.Chain, st.Address, st.Latitude, st.Longitude, st.CityId, st.City.Name, st.Prices.Count))
            .ToListAsync();
        return Page(items, total, s, l);
    }
    
    [HttpGet("nearby")]
    public async Task<ActionResult<IReadOnlyList<NearbyStoreDto>>> Nearby(double lat, double lng, double radiusKm = 3, string? chain = null)
    {
        if (lat is < -90 or > 90) return FieldError(nameof(lat), "Latitude must be between -90 and 90.");
        if (lng is < -180 or > 180) return FieldError(nameof(lng), "Longitude must be between -180 and 180.");
        radiusKm = Math.Clamp(radiusKm, 0.2, 25);

        var dLat = radiusKm / 111.0;
        var dLng = radiusKm / (111.0 * Math.Max(0.1, Math.Cos(lat * Math.PI / 180)));

        var query = db.Stores.AsNoTracking()
            .Where(s => s.Latitude >= lat - dLat && s.Latitude <= lat + dLat && s.Longitude >= lng - dLng && s.Longitude <= lng + dLng);
        if (!string.IsNullOrWhiteSpace(chain)) query = query.Where(s => s.Chain == chain);

        var boxed = await query
            .Select(s => new { s.Id, s.Name, s.Chain, s.Address, s.Latitude, s.Longitude, s.CityId, CityName = s.City.Name, PriceCount = s.Prices.Count })
            .ToListAsync();

        return boxed
            .Select(s => new NearbyStoreDto(s.Id, s.Name, s.Chain, s.Address, s.Latitude, s.Longitude, s.CityId, s.CityName, s.PriceCount,
                Math.Round(Data.Import.OsmStoresImporter.DistanceKm(lat, lng, s.Latitude, s.Longitude), 2)))
            .Where(s => s.DistanceKm <= radiusKm)   
            .OrderBy(s => s.DistanceKm)
            .Take(300)
            .ToList();
    }

    [HttpGet("{id:int}")]
    [OutputCache(PolicyName = CacheTags.Policy, Tags = [CacheTags.Stores])]
    public async Task<ActionResult<StoreDto>> Get(int id)
    {
        var store = await Query(id).FirstOrDefaultAsync();
        return store is null ? NotFound() : store;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<StoreDto>> Create(StoreRequest request)
    {
        if (!await db.Cities.AnyAsync(c => c.Id == request.CityId))
            return FieldError(nameof(request.CityId), "City not found.");

        var store = new Store();
        Apply(request, store);
        db.Stores.Add(store);
        await db.SaveChangesAsync();
        await cache.StoreChangedAsync();
        return CreatedAtAction(nameof(Get), new { id = store.Id }, await Query(store.Id).FirstAsync());
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<StoreDto>> Update(int id, StoreRequest request)
    {
        var store = await db.Stores.FindAsync(id);
        if (store is null) return NotFound();
        if (!await db.Cities.AnyAsync(c => c.Id == request.CityId))
            return FieldError(nameof(request.CityId), "City not found.");

        Apply(request, store);
        await db.SaveChangesAsync();
        await cache.StoreChangedAsync();
        return await Query(id).FirstAsync();
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id)
    {
        var store = await db.Stores.FindAsync(id);
        if (store is null) return NotFound();

        db.Stores.Remove(store); 
        await db.SaveChangesAsync();
        await cache.StoreChangedAsync();
        return NoContent();
    }
    
    [HttpGet("{storeId:int}/prices")]
    [OutputCache(PolicyName = CacheTags.Policy, Tags = [CacheTags.Stores])]
    public async Task<ActionResult<PagedResult<StorePriceDto>>> Prices(int storeId, int? skip = null, int? limit = null)
    {
        if (!await db.Stores.AnyAsync(st => st.Id == storeId)) return NotFound();

        var (s, l) = Normalize(skip, limit);
        var query = db.StorePrices.AsNoTracking().Where(sp => sp.StoreId == storeId);
        var total = await query.CountAsync();
        var items = await query.OrderBy(sp => sp.Product.Name).ThenBy(sp => sp.ProductId).Skip(s).Take(l)
            .Select(sp => new StorePriceDto(sp.ProductId, sp.Product.Name, sp.PricePerKg, sp.UpdatedAt, sp.Source))
            .ToListAsync();
        return Page(items, total, s, l);
    }
    
    [HttpPut("{storeId:int}/prices/{productId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<StorePriceDto>> SetPrice(int storeId, int productId, PriceRequest request)
    {
        if (!await db.Stores.AnyAsync(st => st.Id == storeId)) return NotFound();
        var product = await db.Products.FindAsync(productId);
        if (product is null) return NotFound();

        var price = await db.StorePrices.FindAsync(storeId, productId);
        var created = price is null;
        if (created)
        {
            price = new StorePrice { StoreId = storeId, ProductId = productId };
            db.StorePrices.Add(price);
        }
        price!.PricePerKg = request.PricePerKg;
        price.UpdatedAt = DateTime.UtcNow;
        price.Source = PriceSource.User;
        await db.SaveChangesAsync();
        await cache.PriceChangedAsync();

        var dto = new StorePriceDto(productId, product.Name, price.PricePerKg, price.UpdatedAt);
        return created ? CreatedAtAction(nameof(Prices), new { storeId }, dto) : dto;
    }

    [HttpDelete("{storeId:int}/prices/{productId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeletePrice(int storeId, int productId)
    {
        var price = await db.StorePrices.FindAsync(storeId, productId);
        if (price is null) return NotFound();

        db.StorePrices.Remove(price);
        await db.SaveChangesAsync();
        await cache.PriceChangedAsync();
        return NoContent();
    }

    private IQueryable<StoreDto> Query(int id) =>
        db.Stores.AsNoTracking().Where(st => st.Id == id)
            .Select(st => new StoreDto(st.Id, st.Name, st.Chain, st.Address, st.Latitude, st.Longitude, st.CityId, st.City.Name, st.Prices.Count));

    private static void Apply(StoreRequest r, Store s)
    {
        s.Name = r.Name.Trim();
        s.Chain = r.Chain.Trim();
        s.Address = string.IsNullOrWhiteSpace(r.Address) ? null : r.Address.Trim();
        s.Latitude = r.Latitude;
        s.Longitude = r.Longitude;
        s.CityId = r.CityId;
    }
}

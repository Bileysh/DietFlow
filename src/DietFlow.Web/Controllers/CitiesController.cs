using DietFlow.Web.Data;
using DietFlow.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DietFlow.Web.Controllers;

public class CitiesController(AppDbContext db, CityContext cityContext) : Controller
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Select(int cityId, string? returnUrl)
    {
        if (await db.Cities.AnyAsync(c => c.Id == cityId))
            cityContext.SetCurrent(cityId);

        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Detect(double lat, double lng)
    {
        if (lat is < -90 or > 90 || lng is < -180 or > 180) return BadRequest();

        var cities = await db.Cities.AsNoTracking().Where(c => c.Stores.Any())
            .Select(c => new { c.Id, c.Name, c.Latitude, c.Longitude }).ToListAsync();
        var nearest = cities.MinBy(c => Data.Import.OsmStoresImporter.DistanceKm(lat, lng, c.Latitude, c.Longitude));
        if (nearest is null) return NotFound();

        cityContext.SetCurrent(nearest.Id);
        var km = Data.Import.OsmStoresImporter.DistanceKm(lat, lng, nearest.Latitude, nearest.Longitude);
        return Json(new { id = nearest.Id, name = nearest.Name, distanceKm = Math.Round(km, 1) });
    }
}

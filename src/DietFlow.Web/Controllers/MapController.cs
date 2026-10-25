using DietFlow.Web.Data;
using DietFlow.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DietFlow.Web.Controllers;


public class MapController(AppDbContext db, CityContext cityContext) : Controller
{
    public async Task<IActionResult> Index(int? productId)
    {
        var city = await cityContext.GetCurrentAsync();
        if (city is null) return NotFound();

        ViewBag.Chains = await db.Stores.AsNoTracking().Where(s => s.CityId == city.Id)
            .Select(s => s.Chain).Distinct().OrderBy(c => c).ToListAsync();

        if (productId is not null)
        {
            ViewBag.ProductName = await db.Products.AsNoTracking().Where(p => p.Id == productId).Select(p => p.Name).FirstOrDefaultAsync();
            if (ViewBag.ProductName is not null) ViewBag.ProductId = productId;
        }
        return View(city);
    }
}

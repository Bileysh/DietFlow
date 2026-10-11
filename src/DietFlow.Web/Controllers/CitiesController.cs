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
}

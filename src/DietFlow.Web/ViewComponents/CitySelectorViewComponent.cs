using DietFlow.Web.Data;
using DietFlow.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace DietFlow.Web.ViewComponents;

public class CitySelectorViewComponent(AppDbContext db, CityContext cityContext) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var current = await cityContext.GetCurrentAsync();
        var cities = await db.Cities.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
        return View(new SelectList(cities, "Id", "Name", current?.Id));
    }
}

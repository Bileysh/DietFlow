using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using DietFlow.Web.Data;
using DietFlow.Web.Models;
using DietFlow.Web.Models.ViewModels;
using DietFlow.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace DietFlow.Web.Controllers;

public class HomeController(AppDbContext db, RecipeCalculator calculator, CityContext cityContext) : Controller
{
    public async Task<IActionResult> Index()
    {
        var city = await cityContext.GetCurrentAsync();
        var recipes = await db.Recipes.AsNoTracking()
            .Where(r => r.PhotoPath != null)
            .OrderBy(r => EF.Functions.Random())
            .Take(4)
            .Select(r => new { r.Id, r.Title, r.Slug, r.PhotoPath, r.CookTimeMinutes })
            .ToListAsync();
        var summaries = await calculator.SummarizeManyAsync(recipes.Select(r => r.Id).ToList(), city?.Id ?? 0);

        return View(new HomeVm
        {
            CityName = city?.Name,
            StoreCount = await db.Stores.CountAsync(),
            CityCount = await db.Cities.CountAsync(),
            RecipeCount = await db.Recipes.CountAsync(),
            Recipes = recipes.Select(r => new HomeRecipeVm(r.Title, r.Slug, r.PhotoPath!, r.CookTimeMinutes,
                summaries.GetValueOrDefault(r.Id)?.Kcal, summaries.GetValueOrDefault(r.Id)?.Cost)).ToList(),
        });
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}

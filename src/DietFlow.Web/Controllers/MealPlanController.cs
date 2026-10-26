using DietFlow.Web.Models.Entities;
using DietFlow.Web.Services;
using DietFlow.Web.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.FeatureManagement.Mvc;

namespace DietFlow.Web.Controllers;

[Authorize]
[FeatureGate(AppFeatures.WeeklyMealPlan)]
public class MealPlanController(UserManager<AppUser> users, WeeklyPlanService planner, CityContext cityContext) : Controller
{
    public async Task<IActionResult> Index()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        var city = await cityContext.GetCurrentAsync();

        var plan = await planner.BuildAsync(user.DailyKcalGoal, city?.Id ?? 0, city?.Name, DateOnly.FromDateTime(AppTime.NowLocal));
        return View(plan);
    }
}

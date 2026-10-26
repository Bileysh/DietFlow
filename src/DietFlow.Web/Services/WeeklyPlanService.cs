using DietFlow.Web.Data;
using DietFlow.Web.Models.Entities;
using DietFlow.Web.Models.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace DietFlow.Web.Services;

public class WeeklyPlanService(AppDbContext db, RecipeCalculator calculator)
{
    private static readonly (MealType Type, decimal Share)[] Slots =
        [(MealType.Breakfast, 0.25m), (MealType.Lunch, 0.40m), (MealType.Dinner, 0.35m)];

    private const decimal Tolerance = 0.15m;

    public async Task<WeeklyPlanVm> BuildAsync(int dailyKcalGoal, int cityId, string? cityName, DateOnly startDate)
    {
        var recipes = await db.Recipes.AsNoTracking()
            .Select(r => new { r.Id, r.Title, r.Slug, r.CookTimeMinutes })
            .ToListAsync();
        var summaries = await calculator.SummarizeManyAsync(recipes.Select(r => r.Id).ToList(), cityId);

        var candidates = recipes
            .Where(r => summaries.TryGetValue(r.Id, out var s) && s.Kcal > 0 && s.Cost is not null)
            .Select(r => (Recipe: r, Summary: summaries[r.Id]))
            .ToList();

        var lastUsedDay = new Dictionary<int, int>();
        var days = new List<PlanDayVm>();
        var shopping = new Dictionary<int, ShoppingLineVm>();

        for (var d = 0; d < 7; d++)
        {
            var meals = new List<PlanMealVm>();
            foreach (var (type, share) in Slots)
            {
                var target = dailyKcalGoal * share;
                var options = candidates
                    .Select(c =>
                    {
                        var portion = Math.Clamp(Math.Round(target / c.Summary.Kcal * 4, MidpointRounding.AwayFromZero) / 4, 0.5m, 2m);
                        return (c.Recipe, c.Summary, Portion: portion, Kcal: c.Summary.Kcal * portion, Cost: c.Summary.Cost!.Value * portion);
                    })
                    .Where(o => !lastUsedDay.TryGetValue(o.Recipe.Id, out var used) || d - used > 2)
                    .ToList();

                var fitting = options.Where(o => Math.Abs(o.Kcal - target) <= target * Tolerance).ToList();
                var pick = fitting.Count > 0
                    ? fitting.OrderBy(o => type == MealType.Breakfast && o.Recipe.CookTimeMinutes > 20 ? 1 : 0).ThenBy(o => o.Cost).First()
                    : options.OrderBy(o => Math.Abs(o.Kcal - target)).FirstOrDefault();
                if (pick.Recipe is null) continue;

                lastUsedDay[pick.Recipe.Id] = d;
                meals.Add(new PlanMealVm(type, pick.Recipe.Id, pick.Recipe.Title, pick.Recipe.Slug, pick.Portion,
                    Math.Round(pick.Kcal), Math.Round(pick.Summary.Protein * pick.Portion, 1), Math.Round(pick.Cost, 2)));

                foreach (var line in pick.Summary.Lines)
                {
                    var grams = line.Grams / pick.Summary.Servings * pick.Portion;
                    shopping[line.ProductId] = shopping.TryGetValue(line.ProductId, out var s)
                        ? s with { Grams = s.Grams + grams }
                        : new ShoppingLineVm(line.ProductId, line.ProductName, grams, line.PricePerKg, line.CheapestStore, line.CheapestStoreId);
                }
            }
            days.Add(new PlanDayVm(startDate.AddDays(d), meals));
        }

        return new WeeklyPlanVm
        {
            DailyKcalGoal = dailyKcalGoal,
            CityName = cityName,
            Days = days,
            Shopping = shopping.Values.OrderBy(s => s.ProductName).ToList(),
            RecipesConsidered = candidates.Count,
        };
    }
}

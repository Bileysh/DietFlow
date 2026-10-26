using System.ComponentModel.DataAnnotations;
using DietFlow.Web.Models.Entities;
using DietFlow.Web.Services;

namespace DietFlow.Web.Models.ViewModels;

public class LoginVm
{
    public string? ReturnUrl { get; init; }
    public IReadOnlyList<(string Name, string DisplayName)> Providers { get; init; } = [];
    public bool DemoLoginAvailable { get; init; }
    public string? Error { get; init; }
}

public class ProfileVm
{
    public AppUser User { get; init; } = null!;
    public bool IsAdmin { get; init; }
    public AnalysisQuota Quota { get; init; } = null!;
    public IReadOnlyList<ApiToken> Tokens { get; init; } = [];
    public string? NewToken { get; init; }
    public IReadOnlyList<string> Logins { get; init; } = [];
}

public class GoalFormVm
{
    [Range(1000, 5000, ErrorMessage = "Денна норма — від 1000 до 5000 ккал.")]
    public int DailyKcalGoal { get; set; }
}

public record PlanMealVm(MealType Type, int RecipeId, string Title, string Slug, decimal Portion, decimal Kcal, decimal Protein, decimal Cost);

public record PlanDayVm(DateOnly Date, IReadOnlyList<PlanMealVm> Meals)
{
    public decimal Kcal => Meals.Sum(m => m.Kcal);
    public decimal Cost => Meals.Sum(m => m.Cost);
}

public record ShoppingLineVm(int ProductId, string ProductName, decimal Grams, decimal? PricePerKg, string? CheapestStore, int? CheapestStoreId)
{
    public decimal? Cost => PricePerKg is null ? null : Math.Round(Grams / 1000m * PricePerKg.Value, 2);
}

public class WeeklyPlanVm
{
    public int DailyKcalGoal { get; init; }
    public string? CityName { get; init; }
    public IReadOnlyList<PlanDayVm> Days { get; init; } = [];
    public IReadOnlyList<ShoppingLineVm> Shopping { get; init; } = [];
    public int RecipesConsidered { get; init; }
    public decimal WeekCost => Days.Sum(d => d.Cost);
}

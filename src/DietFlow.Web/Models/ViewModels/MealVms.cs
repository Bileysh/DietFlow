using System.ComponentModel.DataAnnotations;
using DietFlow.Web.Models.Entities;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace DietFlow.Web.Models.ViewModels;

public class MealFormVm
{
    public int Id { get; set; }

    [Display(Name = "Прийом їжі")]
    public MealType Type { get; set; }

    [Display(Name = "Коли")]
    [DataType(DataType.DateTime)]
    public DateTime EatenAtLocal { get; set; }

    [Display(Name = "Фото страви")]
    public IFormFile? Photo { get; set; }

    [Display(Name = "Прибрати поточне фото")]
    public bool RemovePhoto { get; set; }

    [ValidateNever] public string? CurrentPhotoPath { get; set; }

    [MaxLength(200)]
    [Display(Name = "Страва")]
    public string? DishName { get; set; }

    [Range(0, 10000, ErrorMessage = "Від 0 до 10 000 ккал")]
    [Display(Name = "Ккал")]
    public int? Kcal { get; set; }

    [Range(0, 1000)] [Display(Name = "Білки, г")] public decimal? ProteinG { get; set; }
    [Range(0, 1000)] [Display(Name = "Жири, г")] public decimal? FatG { get; set; }
    [Range(0, 1000)] [Display(Name = "Вуглеводи, г")] public decimal? CarbsG { get; set; }

    [Range(1, 10, ErrorMessage = "Оцінка від 1 до 10")]
    [Display(Name = "Оцінка корисності (1–10)")]
    public int? HealthScore { get; set; }

    [MaxLength(1000)]
    [Display(Name = "Висновок")]
    public string? Verdict { get; set; }
}

public record DiaryDayVm(DateOnly Date, IReadOnlyList<Meal> Meals, int TotalKcal, int Goal);

public static class MealTypeText
{
    public static string Name(MealType t) => t switch
    {
        MealType.Breakfast => "Сніданок",
        MealType.Lunch => "Обід",
        MealType.Dinner => "Вечеря",
        MealType.Snack => "Перекус",
        _ => t.ToString(),
    };
}

public record RecommendationVm(Recipe Recipe, string? Reason, DietFlow.Web.Services.RecipeSummary? Summary);

public class MealDetailsVm
{
    public Meal Meal { get; init; } = null!;
    public DietFlow.Web.Services.Ai.MealAnalysis? Analysis { get; init; }
    public IReadOnlyList<RecommendationVm> Recommendations { get; init; } = [];
    public bool AiAvailable { get; init; }
    public string? CityName { get; init; }
}

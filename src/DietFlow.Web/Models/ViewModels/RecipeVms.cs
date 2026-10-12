using System.ComponentModel.DataAnnotations;
using DietFlow.Web.Models.Entities;
using DietFlow.Web.Services;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace DietFlow.Web.Models.ViewModels;

public class IngredientRowVm
{
    [Range(1, int.MaxValue, ErrorMessage = "Оберіть продукт")]
    public int ProductId { get; set; }

    [Range(1, 5000, ErrorMessage = "Від 1 до 5000 г")]
    public decimal Grams { get; set; }
}

public class RecipeFormVm : IValidatableObject
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Вкажіть назву"), MaxLength(200)]
    [Display(Name = "Назва")]
    public string Title { get; set; } = "";

    [MaxLength(1000)]
    [Display(Name = "Короткий опис")]
    public string? Description { get; set; }

    [Display(Name = "Кроки приготування (Markdown)")]
    public string? StepsMarkdown { get; set; }

    [Display(Name = "Спосіб приготування")]
    public CookingMethod Method { get; set; }

    [Range(1, 1440, ErrorMessage = "Від 1 хв до 24 год")]
    [Display(Name = "Час, хв")]
    public int CookTimeMinutes { get; set; } = 20;

    [Range(1, 50, ErrorMessage = "Від 1 до 50 порцій")]
    [Display(Name = "Порцій")]
    public int Servings { get; set; } = 1;

    [Display(Name = "Фото")]
    public IFormFile? Photo { get; set; }

    [Display(Name = "Прибрати поточне фото")]
    public bool RemovePhoto { get; set; }

    [ValidateNever] public string? CurrentPhotoPath { get; set; }

    public List<IngredientRowVm> Ingredients { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Ingredients.Count == 0)
            yield return new ValidationResult("Додайте хоча б один інгредієнт", [nameof(Ingredients)]);

        if (Ingredients.GroupBy(i => i.ProductId).Any(g => g.Key > 0 && g.Count() > 1))
            yield return new ValidationResult("Один продукт додано двічі — об'єднайте рядки", [nameof(Ingredients)]);
    }
}

public record RecipeCardVm(Recipe Recipe, RecipeSummary? Summary);

public class RecipeIndexVm
{
    public IReadOnlyList<RecipeCardVm> Items { get; init; } = [];
    public CookingMethod? Method { get; init; }
    public int? MaxKcal { get; init; }
    public string Sort { get; init; } = "title";
    public string? CityName { get; init; }
}

public class RecipeDetailsVm
{
    public Recipe Recipe { get; init; } = null!;
    public RecipeSummary? Summary { get; init; }
    public string? CityName { get; init; }
}

public static class CookingMethodText
{
    public static string Name(CookingMethod m) => m switch
    {
        CookingMethod.Raw => "Без термообробки",
        CookingMethod.Boiled => "Варіння",
        CookingMethod.Steamed => "На пару",
        CookingMethod.Baked => "Запікання",
        CookingMethod.Grilled => "Гриль",
        CookingMethod.Fried => "Смаження",
        CookingMethod.Stewed => "Тушкування",
        _ => m.ToString(),
    };

    public static string Icon(CookingMethod m) => m switch
    {
        CookingMethod.Raw => "🥗",
        CookingMethod.Boiled => "🍲",
        CookingMethod.Steamed => "♨️",
        CookingMethod.Baked => "🔥",
        CookingMethod.Grilled => "🍢",
        CookingMethod.Fried => "🍳",
        CookingMethod.Stewed => "🥘",
        _ => "🍽️",
    };
}

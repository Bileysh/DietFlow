using System.ComponentModel.DataAnnotations;

namespace DietFlow.Web.Models.Entities;

public enum CookingMethod
{
    Raw = 0,
    Boiled = 1,
    Steamed = 2,
    Baked = 3,
    Grilled = 4,
    Fried = 5,
    Stewed = 6
}

public class Recipe
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = null!;

    [Required, MaxLength(200)]
    public string Slug { get; set; } = null!;

    [MaxLength(1000)]
    public string? Description { get; set; }

    public string? StepsMarkdown { get; set; }

    public CookingMethod Method { get; set; }
    public int CookTimeMinutes { get; set; }
    public int Servings { get; set; } = 1;

    [MaxLength(500)]
    public string? PhotoPath { get; set; }

    public ICollection<RecipeIngredient> Ingredients { get; set; } = new List<RecipeIngredient>();
    public ICollection<MealRecommendation> RecommendedFor { get; set; } = new List<MealRecommendation>();
}

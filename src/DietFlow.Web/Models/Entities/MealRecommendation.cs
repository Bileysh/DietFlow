using System.ComponentModel.DataAnnotations;

namespace DietFlow.Web.Models.Entities;

public class MealRecommendation
{
    public int MealId { get; set; }
    public Meal Meal { get; set; } = null!;

    public int RecipeId { get; set; }
    public Recipe Recipe { get; set; } = null!;

    [MaxLength(500)]
    public string? Reason { get; set; }
}

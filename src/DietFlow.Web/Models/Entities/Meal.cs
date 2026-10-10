using System.ComponentModel.DataAnnotations;

namespace DietFlow.Web.Models.Entities;

public enum MealType
{
    Breakfast = 0,
    Lunch = 1,
    Dinner = 2,
    Snack = 3
}

public class Meal
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = null!;
    public AppUser User { get; set; } = null!;
    public MealType Type { get; set; }
    public DateTime EatenAt { get; set; } = DateTime.UtcNow;

    [MaxLength(500)]
    public string? PhotoPath { get; set; }

    [MaxLength(200)]
    public string? DishName { get; set; }
    public int? Kcal { get; set; }
    public decimal? ProteinG { get; set; }
    public decimal? FatG { get; set; }
    public decimal? CarbsG { get; set; }

    [Range(1, 10)]
    public int? HealthScore { get; set; }
    
    [MaxLength(1000)]
    public string? Verdict { get; set; }
    public string? AnalysisJson { get; set; }
    public ICollection<MealRecommendation> Recommendations { get; set; } = new List<MealRecommendation>();
}

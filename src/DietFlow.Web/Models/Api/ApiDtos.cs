using System.ComponentModel.DataAnnotations;
using DietFlow.Web.Models.Entities;

namespace DietFlow.Web.Models.Api;


public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Skip, int Limit, string? NextLink);

public record CityDto(int Id, string Name, double Latitude, double Longitude);

public record ProductDto(
    int Id, string Name, string? Category,
    decimal KcalPer100g, decimal ProteinPer100g, decimal FatPer100g, decimal CarbsPer100g,
    string? Barcode, decimal? MinPricePerKg);

public record ProductSearchItem(int Id, string Text, decimal Kcal, string? Category);
public record SearchPagination(bool More);
public record ProductSearchResponse(IReadOnlyList<ProductSearchItem> Results, SearchPagination Pagination);

public record ProductPriceDto(int StoreId, string StoreName, string Chain, int CityId, decimal PricePerKg, DateTime UpdatedAt);

public record ProductDetailsDto(ProductDto Product, IReadOnlyList<ProductPriceDto> Prices, IReadOnlyList<string> UsedInRecipes);

public class ProductRequest : IValidatableObject
{
    [Required, MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(100)] public string? Category { get; set; }
    [Range(0, 900)] public decimal KcalPer100g { get; set; }
    [Range(0, 100)] public decimal ProteinPer100g { get; set; }
    [Range(0, 100)] public decimal FatPer100g { get; set; }
    [Range(0, 100)] public decimal CarbsPer100g { get; set; }
    [MaxLength(50)] public string? Barcode { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ProteinPer100g + FatPer100g + CarbsPer100g > 100)
            yield return new ValidationResult("Sum of protein, fat and carbs cannot exceed 100 g per 100 g",
                [nameof(ProteinPer100g), nameof(FatPer100g), nameof(CarbsPer100g)]);
    }
}

public record StoreDto(int Id, string Name, string Chain, string? Address, double Latitude, double Longitude, int CityId, string CityName, int PriceCount);

public class StoreRequest
{
    [Required, MaxLength(200)] public string Name { get; set; } = "";
    [Required, MaxLength(100)] public string Chain { get; set; } = "";
    [MaxLength(300)] public string? Address { get; set; }
    [Range(-90, 90)] public double Latitude { get; set; }
    [Range(-180, 180)] public double Longitude { get; set; }
    [Range(1, int.MaxValue)] public int CityId { get; set; }
}

public record StorePriceDto(int ProductId, string ProductName, decimal PricePerKg, DateTime UpdatedAt);

public class PriceRequest
{
    [Range(0.01, 100000)] public decimal PricePerKg { get; set; }
}

public record RecipeSummaryDto(decimal Kcal, decimal Protein, decimal Fat, decimal Carbs, decimal? Cost, bool CostIsComplete, IReadOnlyList<string> MissingPrices);

public record RecipeDto(
    int Id, string Title, string Slug, string? Description, CookingMethod Method,
    int CookTimeMinutes, int Servings, string? PhotoUrl, RecipeSummaryDto? PerServing);

public record RecipeIngredientDto(int ProductId, string ProductName, decimal Grams, decimal Kcal, decimal? PricePerKg, string? CheapestStore, decimal? Cost);

public record RecipeDetailsDto(RecipeDto Recipe, string? StepsMarkdown, IReadOnlyList<RecipeIngredientDto> Ingredients);

public class IngredientRequest
{
    [Range(1, int.MaxValue)] public int ProductId { get; set; }
    [Range(1, 5000)] public decimal Grams { get; set; }
}

public class RecipeRequest : IValidatableObject
{
    [Required, MaxLength(200)] public string Title { get; set; } = "";
    [MaxLength(1000)] public string? Description { get; set; }
    public string? StepsMarkdown { get; set; }
    public CookingMethod Method { get; set; }
    [Range(1, 1440)] public int CookTimeMinutes { get; set; } = 20;
    [Range(1, 50)] public int Servings { get; set; } = 1;
    public List<IngredientRequest> Ingredients { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Ingredients.Count == 0)
            yield return new ValidationResult("At least one ingredient is required", [nameof(Ingredients)]);
        if (Ingredients.GroupBy(i => i.ProductId).Any(g => g.Count() > 1))
            yield return new ValidationResult("Each product can appear only once", [nameof(Ingredients)]);
    }
}

public record MealRecommendationDto(int RecipeId, string RecipeTitle, string? Reason);

public record MealDto(
    int Id, MealType Type, DateTime EatenAt, string? PhotoUrl,
    string? DishName, int? Kcal, decimal? ProteinG, decimal? FatG, decimal? CarbsG,
    int? HealthScore, string? Verdict, bool Analyzed, IReadOnlyList<MealRecommendationDto> Recommendations);

public class MealRequest
{
    public MealType Type { get; set; }
    public DateTime? EatenAt { get; set; }
    [MaxLength(200)] public string? DishName { get; set; }
    [Range(0, 10000)] public int? Kcal { get; set; }
    [Range(0, 1000)] public decimal? ProteinG { get; set; }
    [Range(0, 1000)] public decimal? FatG { get; set; }
    [Range(0, 1000)] public decimal? CarbsG { get; set; }
    [Range(1, 10)] public int? HealthScore { get; set; }
    [MaxLength(1000)] public string? Verdict { get; set; }
}

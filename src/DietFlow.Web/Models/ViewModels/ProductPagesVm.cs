using DietFlow.Web.Models.Entities;

namespace DietFlow.Web.Models.ViewModels;

public record ProductListItemVm(
    int Id, string Name, string? Category,
    decimal Kcal, decimal Protein, decimal Fat, decimal Carbs,
    decimal? MinPrice, int StoreCount);

public class ProductIndexVm
{
    public IReadOnlyList<ProductListItemVm> Items { get; init; } = [];
    public IReadOnlyList<string> Categories { get; init; } = [];
    public string? Q { get; init; }
    public string? Category { get; init; }
    public string Sort { get; init; } = "name";
    public int Page { get; init; }
    public int TotalPages { get; init; }
    public int TotalCount { get; init; }
    public string? CityName { get; init; }
}

public record ProductPriceRowVm(int StoreId, string StoreName, string Chain, string? Address, decimal PricePerKg, PriceSource Source, DateTime UpdatedAt);

public class ProductDetailsVm
{
    public Product Product { get; init; } = null!;
    public string? CityName { get; init; }
    public IReadOnlyList<ProductPriceRowVm> Prices { get; init; } = [];
    public IReadOnlyList<string> UsedInRecipes { get; init; } = [];
    public OfficialPriceVm? Official { get; init; }
}

public record OfficialPriceVm(string RegionName, string StatName, decimal PricePerKg, DateOnly Period);

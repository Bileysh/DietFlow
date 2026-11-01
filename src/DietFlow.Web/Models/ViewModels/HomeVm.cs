namespace DietFlow.Web.Models.ViewModels;

public record HomeRecipeVm(string Title, string Slug, string PhotoPath, int CookTimeMinutes, decimal? Kcal, decimal? Cost);

public class HomeVm
{
    public string? CityName { get; init; }
    public int StoreCount { get; init; }
    public int CityCount { get; init; }
    public int RecipeCount { get; init; }
    public IReadOnlyList<HomeRecipeVm> Recipes { get; init; } = [];
}

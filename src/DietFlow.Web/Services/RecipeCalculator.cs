using DietFlow.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace DietFlow.Web.Services;

public record IngredientLine(
    int ProductId, string ProductName, decimal Grams,
    decimal Kcal, decimal Protein, decimal Fat, decimal Carbs,
    decimal? PricePerKg, string? CheapestStore, int? CheapestStoreId, decimal? Cost);

public record RecipeSummary(
    int RecipeId, int Servings,
    decimal Kcal, decimal Protein, decimal Fat, decimal Carbs,         
    decimal? Cost,                                                      
    IReadOnlyList<string> MissingPrices,                                
    IReadOnlyList<IngredientLine> Lines)
{
    public bool CostIsComplete => MissingPrices.Count == 0;
}

public class RecipeCalculator(AppDbContext db)
{
    public async Task<RecipeSummary?> SummarizeAsync(int recipeId, int cityId) =>
        (await SummarizeManyAsync([recipeId], cityId)).GetValueOrDefault(recipeId);
    
    public async Task<Dictionary<int, RecipeSummary>> SummarizeManyAsync(IReadOnlyCollection<int> recipeIds, int cityId)
    {
        var rows = await db.RecipeIngredients.AsNoTracking()
            .Where(ri => recipeIds.Contains(ri.RecipeId))
            .Select(ri => new
            {
                ri.RecipeId,
                ri.Recipe.Servings,
                ri.ProductId,
                ri.Product.Name,
                ri.Grams,
                ri.Product.KcalPer100g,
                ri.Product.ProteinPer100g,
                ri.Product.FatPer100g,
                ri.Product.CarbsPer100g,
                Cheapest = ri.Product.Prices
                    .Where(sp => sp.Store.CityId == cityId)
                    .OrderBy(sp => sp.PricePerKg)
                    .Select(sp => new { sp.PricePerKg, sp.Store.Name, sp.StoreId })
                    .FirstOrDefault(),
            })
            .ToListAsync();

        var result = new Dictionary<int, RecipeSummary>();
        foreach (var group in rows.GroupBy(r => r.RecipeId))
        {
            var servings = Math.Max(1, group.First().Servings);

            var lines = group
                .OrderByDescending(r => r.Grams)
                .Select(r =>
                {
                    var k = r.Grams / 100m; 
                    var cost = r.Cheapest is null ? (decimal?)null : r.Grams / 1000m * r.Cheapest.PricePerKg; 
                    return new IngredientLine(
                        r.ProductId, r.Name, r.Grams,
                        r.KcalPer100g * k, r.ProteinPer100g * k, r.FatPer100g * k, r.CarbsPer100g * k,
                        r.Cheapest?.PricePerKg, r.Cheapest?.Name, r.Cheapest?.StoreId, cost);
                })
                .ToList();

            var missing = lines.Where(l => l.Cost is null).Select(l => l.ProductName).ToList();
            var knownCost = lines.Where(l => l.Cost is not null).Sum(l => l.Cost!.Value);

            result[group.Key] = new RecipeSummary(
                group.Key, servings,
                Math.Round(lines.Sum(l => l.Kcal) / servings, 0),
                Math.Round(lines.Sum(l => l.Protein) / servings, 1),
                Math.Round(lines.Sum(l => l.Fat) / servings, 1),
                Math.Round(lines.Sum(l => l.Carbs) / servings, 1),
                missing.Count == lines.Count ? null : Math.Round(knownCost / servings, 2),
                missing, lines);
        }
        return result;
    }
}

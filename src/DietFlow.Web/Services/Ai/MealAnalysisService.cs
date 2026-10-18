using System.Text.Json;
using DietFlow.Web.Data;
using DietFlow.Web.Models.Entities;
using DietFlow.Web.Models.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace DietFlow.Web.Services.Ai;


public class MealAnalysisService(
    AppDbContext db, IMealAnalyzer analyzer, RecipeCalculator calculator, IFileStorage files)
{
    public bool IsAvailable => analyzer.IsConfigured;
    public string ProviderName => analyzer.ProviderName;

    public async Task AnalyzeMealAsync(int mealId, string userId, int cityId, CancellationToken ct = default)
    {
        var meal = await db.Meals.Include(m => m.Recommendations)
                       .FirstOrDefaultAsync(m => m.Id == mealId && m.UserId == userId, ct)
                   ?? throw new MealAnalysisException("Прийом їжі не знайдено.", canRetry: false);

        var image = await files.ReadAsync(meal.PhotoPath, ct)
                    ?? throw new MealAnalysisException("Спершу додайте фото страви.", canRetry: false);
        var mediaType = ImageValidator.DetectMediaType(image)
                        ?? throw new MealAnalysisException("Фото має бути у форматі JPEG, PNG або WebP.", canRetry: false);

        var catalog = await BuildCatalogAsync(cityId, ct);
        var analysis = await analyzer.AnalyzeAsync(image, mediaType, catalog, ct);

        Apply(meal, analysis, catalog.Select(c => c.Id).ToHashSet());
        await db.SaveChangesAsync(ct);
    }

    private async Task<List<CatalogRecipe>> BuildCatalogAsync(int cityId, CancellationToken ct)
    {
        var recipes = await db.Recipes.AsNoTracking()
            .Select(r => new { r.Id, r.Title, r.Method })
            .ToListAsync(ct);
        var summaries = await calculator.SummarizeManyAsync(recipes.Select(r => r.Id).ToList(), cityId);

        return recipes
            .Where(r => summaries.ContainsKey(r.Id))
            .Select(r =>
            {
                var s = summaries[r.Id];
                return new CatalogRecipe(r.Id, r.Title, s.Kcal, s.Protein, s.Fat, s.Cost, CookingMethodText.Name(r.Method));
            })
            .ToList();
    }

    private static void Apply(Meal meal, MealAnalysis a, IReadOnlySet<int> validRecipeIds)
    {
        meal.AnalysisJson = JsonSerializer.Serialize(a, MealAnalysisPrompt.Json);
        meal.DishName = Trim(a.DishName, 200);
        meal.Verdict = Trim(a.Verdict, 1000);

        if (a.IsFood)
        {
            meal.Kcal = Math.Clamp(a.Kcal, 0, 10000);
            meal.ProteinG = Math.Clamp(a.ProteinG, 0, 1000);
            meal.FatG = Math.Clamp(a.FatG, 0, 1000);
            meal.CarbsG = Math.Clamp(a.CarbsG, 0, 1000);
            meal.HealthScore = Math.Clamp(a.HealthScore, 1, 10);
        }
        else
        {
            meal.Kcal = null; meal.ProteinG = null; meal.FatG = null; meal.CarbsG = null; meal.HealthScore = null;
        }

        var wanted = a.RecommendedRecipes
            .Where(r => validRecipeIds.Contains(r.RecipeId))
            .DistinctBy(r => r.RecipeId).Take(3)
            .ToDictionary(r => r.RecipeId, r => Trim(r.Reason, 500));
        
        foreach (var existing in meal.Recommendations.ToList())
        {
            if (wanted.Remove(existing.RecipeId, out var reason)) existing.Reason = reason;
            else meal.Recommendations.Remove(existing);
        }
        foreach (var (recipeId, reason) in wanted)
            meal.Recommendations.Add(new MealRecommendation { RecipeId = recipeId, Reason = reason });
    }

    private static string? Trim(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Length <= max ? s.Trim() : s[..max].Trim();

    public static MealAnalysis? ReadStored(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<MealAnalysis>(json, MealAnalysisPrompt.Json); }
        catch (JsonException) { return null; }
    }
}

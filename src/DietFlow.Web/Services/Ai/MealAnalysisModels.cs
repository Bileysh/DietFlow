namespace DietFlow.Web.Services.Ai;

public interface IMealAnalyzer
{
    bool IsConfigured { get; }
    string ProviderName { get; }

    Task<MealAnalysis> AnalyzeAsync(byte[] image, string mediaType, IReadOnlyList<CatalogRecipe> catalog, CancellationToken ct = default);
}

public class AiOptions
{
    public string Provider { get; set; } = "Gemini";
}

public record CatalogRecipe(int Id, string Title, decimal Kcal, decimal Protein, decimal Fat, decimal? Cost, string Method);

public record AnalyzedIngredient(string Name, decimal Grams);
public record RecommendedRecipe(int RecipeId, string Reason);

public record MealAnalysis(
    bool IsFood,
    string DishName,
    string Confidence,
    IReadOnlyList<AnalyzedIngredient> Ingredients,
    int Kcal,
    decimal ProteinG,
    decimal FatG,
    decimal CarbsG,
    int HealthScore,
    string Verdict,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> Improvements,
    IReadOnlyList<RecommendedRecipe> RecommendedRecipes);

public class MealAnalysisException(string userMessage, bool canRetry, Exception? inner = null)
    : Exception(userMessage, inner)
{
    public bool CanRetry { get; } = canRetry;
}

public class AnalysisLimitException(int limit)
    : MealAnalysisException($"На плані Free доступно {limit} AI-аналізів на день — ліміт на сьогодні вичерпано. " +
                            "Premium знімає обмеження.", canRetry: false)
{
    public int Limit { get; } = limit;
}

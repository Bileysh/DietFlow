using System.Collections.Concurrent;
using DietFlow.Web.Services;
using DietFlow.Web.Services.Ai;
using Microsoft.AspNetCore.Http;

namespace DietFlow.Tests;

public class FakeMealAnalyzer : IMealAnalyzer
{
    public bool IsConfigured => true;
    public string ProviderName => "Fake";

    public Task<MealAnalysis> AnalyzeAsync(byte[] image, string mediaType, IReadOnlyList<CatalogRecipe> catalog, CancellationToken ct = default) =>
        Task.FromResult(new MealAnalysis(
            IsFood: true, DishName: "Тестова страва", Confidence: "high",
            Ingredients: [new AnalyzedIngredient("Курка", 150)],
            Kcal: 400, ProteinG: 30, FatG: 10, CarbsG: 40, HealthScore: 7,
            Verdict: "Збалансовано", Issues: [], Improvements: [], RecommendedRecipes: []));
}

public class InMemoryFileStorage : IFileStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new();

    public async Task<string> SaveImageAsync(IFormFile file, string folder, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var path = $"/uploads/{folder}/{Guid.NewGuid():N}.jpg";
        _files[path] = ms.ToArray();
        return path;
    }

    public Task DeleteAsync(string? publicPath)
    {
        if (publicPath is not null) _files.TryRemove(publicPath, out _);
        return Task.CompletedTask;
    }

    public Task<byte[]?> ReadAsync(string? publicPath, CancellationToken ct = default) =>
        Task.FromResult(publicPath is not null && _files.TryGetValue(publicPath, out var bytes) ? bytes : null);
}

using Microsoft.AspNetCore.OutputCaching;

namespace DietFlow.Web.Services;

public static class CacheTags
{
    public const string Policy = "api";
    public const string Cities = "cities";
    public const string Products = "products";
    public const string Stores = "stores";
    public const string Recipes = "recipes";
}

public class CacheInvalidator(IOutputCacheStore store)
{
    public Task ProductChangedAsync(CancellationToken ct = default) => Evict(ct, CacheTags.Products, CacheTags.Recipes);
    public Task PriceChangedAsync(CancellationToken ct = default) => Evict(ct, CacheTags.Products, CacheTags.Stores, CacheTags.Recipes);
    public Task StoreChangedAsync(CancellationToken ct = default) => Evict(ct, CacheTags.Stores, CacheTags.Products, CacheTags.Recipes);
    public Task RecipeChangedAsync(CancellationToken ct = default) => Evict(ct, CacheTags.Recipes);

    private async Task Evict(CancellationToken ct, params string[] tags)
    {
        foreach (var tag in tags)
            await store.EvictByTagAsync(tag, ct);
    }
}

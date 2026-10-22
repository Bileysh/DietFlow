using DietFlow.Web.Data;
using DietFlow.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace DietFlow.Web.Services;

public static class DomainRules
{
    public static Task<bool> BarcodeTakenAsync(AppDbContext db, string? barcode, int exceptProductId)
    {
        if (string.IsNullOrWhiteSpace(barcode)) return Task.FromResult(false);
        var b = barcode.Trim();
        return db.Products.AnyAsync(p => p.Barcode == b && p.Id != exceptProductId);
    }

    public static Task<List<string>> RecipesUsingProductAsync(AppDbContext db, int productId) =>
        db.RecipeIngredients.Where(ri => ri.ProductId == productId)
            .Select(ri => ri.Recipe.Title).OrderBy(t => t).ToListAsync();

    public static async Task<string> UniqueRecipeSlugAsync(AppDbContext db, string title, int? exceptRecipeId)
    {
        var baseSlug = SlugGenerator.Slugify(title);
        if (baseSlug is "create" or "index") baseSlug += "-recipe";
        var slug = baseSlug;
        for (var n = 2; await db.Recipes.AnyAsync(r => r.Slug == slug && r.Id != exceptRecipeId); n++)
            slug = $"{baseSlug}-{n}";
        return slug;
    }
    
    public static void SyncIngredients(Recipe recipe, IEnumerable<(int ProductId, decimal Grams)> desired)
    {
        var wanted = desired.ToDictionary(i => i.ProductId, i => i.Grams);
        foreach (var existing in recipe.Ingredients.ToList())
        {
            if (wanted.Remove(existing.ProductId, out var grams)) existing.Grams = grams;
            else recipe.Ingredients.Remove(existing);
        }
        foreach (var (productId, grams) in wanted)
            recipe.Ingredients.Add(new RecipeIngredient { ProductId = productId, Grams = grams });
    }
}

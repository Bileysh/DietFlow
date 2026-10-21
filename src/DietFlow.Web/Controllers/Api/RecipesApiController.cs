using DietFlow.Web.Data;
using DietFlow.Web.Models.Api;
using DietFlow.Web.Models.Entities;
using DietFlow.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;

namespace DietFlow.Web.Controllers.Api;

[Route("api/v1/recipes")]
[Tags("Recipes")]
public class RecipesApiController(AppDbContext db, RecipeCalculator calculator, CacheInvalidator cache) : ApiControllerBase
{

    [HttpGet]
    [OutputCache(PolicyName = CacheTags.Policy, Tags = [CacheTags.Recipes])]
    public async Task<PagedResult<RecipeDto>> List(
        CookingMethod? method, int? maxKcal, int? cityId, string sort = "title", int? skip = null, int? limit = null)
    {
        var (s, l) = Normalize(skip, limit);
        var query = db.Recipes.AsNoTracking();
        if (method is not null) query = query.Where(r => r.Method == method);
        var recipes = await query.ToListAsync();
        
        var summaries = await calculator.SummarizeManyAsync(recipes.Select(r => r.Id).ToList(), cityId ?? 0);
        var rows = recipes
            .Select(r => (Recipe: r, Summary: summaries.GetValueOrDefault(r.Id)))
            .Where(x => maxKcal is null || (x.Summary?.Kcal ?? 0) <= maxKcal);

        var ordered = sort switch
        {
            "kcal" => rows.OrderBy(x => x.Summary?.Kcal ?? decimal.MaxValue),
            "-protein" => rows.OrderByDescending(x => x.Summary?.Protein ?? 0),
            "cost" => rows.OrderBy(x => x.Summary?.Cost ?? decimal.MaxValue),
            "time" => rows.OrderBy(x => x.Recipe.CookTimeMinutes),
            _ => rows.OrderBy(x => x.Recipe.Title),
        };
        var list = ordered.ThenBy(x => x.Recipe.Id).ToList();

        return Page(list.Skip(s).Take(l).Select(x => Map(x.Recipe, x.Summary)).ToList(), list.Count, s, l);
    }

    [HttpGet("{id:int}")]
    [OutputCache(PolicyName = CacheTags.Policy, Tags = [CacheTags.Recipes])]
    public async Task<ActionResult<RecipeDetailsDto>> Get(int id, int? cityId)
    {
        var recipe = await db.Recipes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        if (recipe is null) return NotFound();
        return await Details(recipe, cityId);
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<RecipeDetailsDto>> Create(RecipeRequest request)
    {
        if (await MissingProducts(request) is { } error) return error;

        var recipe = new Recipe { Slug = await DomainRules.UniqueRecipeSlugAsync(db, request.Title, null) };
        Apply(request, recipe);
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();
        await cache.RecipeChangedAsync();
        return CreatedAtAction(nameof(Get), new { id = recipe.Id }, await Details(recipe, null));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<RecipeDetailsDto>> Update(int id, RecipeRequest request)
    {
        var recipe = await db.Recipes.Include(r => r.Ingredients).FirstOrDefaultAsync(r => r.Id == id);
        if (recipe is null) return NotFound();
        if (await MissingProducts(request) is { } error) return error;

        if (!string.Equals(recipe.Title, request.Title.Trim(), StringComparison.Ordinal))
            recipe.Slug = await DomainRules.UniqueRecipeSlugAsync(db, request.Title, id);
        Apply(request, recipe);
        await db.SaveChangesAsync();
        await cache.RecipeChangedAsync();
        return await Details(recipe, null);
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id)
    {
        var recipe = await db.Recipes.FindAsync(id);
        if (recipe is null) return NotFound();

        db.Recipes.Remove(recipe);
        await db.SaveChangesAsync();
        await cache.RecipeChangedAsync();
        return NoContent();
    }

    private async Task<ActionResult?> MissingProducts(RecipeRequest request)
    {
        var ids = request.Ingredients.Select(i => i.ProductId).Distinct().ToList();
        var found = await db.Products.Where(p => ids.Contains(p.Id)).Select(p => p.Id).ToListAsync();
        var missing = ids.Except(found).ToList();
        return missing.Count == 0 ? null : FieldError(nameof(request.Ingredients), $"Products not found: {string.Join(", ", missing)}.");
    }

    private async Task<RecipeDetailsDto> Details(Recipe recipe, int? cityId)
    {
        var summary = await calculator.SummarizeAsync(recipe.Id, cityId ?? 0);
        var lines = summary?.Lines
            .Select(x => new RecipeIngredientDto(x.ProductId, x.ProductName, x.Grams, Math.Round(x.Kcal, 1), x.PricePerKg, x.CheapestStore,
                x.Cost is { } c ? Math.Round(c, 2) : null))
            .ToList() ?? [];
        return new RecipeDetailsDto(Map(recipe, summary), recipe.StepsMarkdown, lines);
    }

    private static RecipeDto Map(Recipe r, RecipeSummary? s) => new(
        r.Id, r.Title, r.Slug, r.Description, r.Method, r.CookTimeMinutes, r.Servings, r.PhotoPath,
        s is null ? null : new RecipeSummaryDto(s.Kcal, s.Protein, s.Fat, s.Carbs, s.Cost, s.CostIsComplete, s.MissingPrices));

    private static void Apply(RecipeRequest r, Recipe recipe)
    {
        recipe.Title = r.Title.Trim();
        recipe.Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
        recipe.StepsMarkdown = string.IsNullOrWhiteSpace(r.StepsMarkdown) ? null : r.StepsMarkdown.Trim();
        recipe.Method = r.Method;
        recipe.CookTimeMinutes = r.CookTimeMinutes;
        recipe.Servings = r.Servings;
        DomainRules.SyncIngredients(recipe, r.Ingredients.Select(i => (i.ProductId, i.Grams)));
    }
}

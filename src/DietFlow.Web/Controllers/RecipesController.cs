using DietFlow.Web.Data;
using DietFlow.Web.Models.Entities;
using DietFlow.Web.Models.ViewModels;
using DietFlow.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace DietFlow.Web.Controllers;

public class RecipesController(
    AppDbContext db, CityContext cityContext, RecipeCalculator calculator, IFileStorage files, CacheInvalidator cache) : Controller
{
    private const string PhotoFolder = "recipes";

    public async Task<IActionResult> Index(CookingMethod? method, int? maxKcal, string sort = "title")
    {
        var city = await cityContext.GetCurrentAsync();

        var query = db.Recipes.AsNoTracking();
        if (method is not null) query = query.Where(r => r.Method == method);
        var recipes = await query.ToListAsync();

        var summaries = await calculator.SummarizeManyAsync(recipes.Select(r => r.Id).ToList(), city?.Id ?? 0);
        
        var cards = recipes
            .Select(r => new RecipeCardVm(r, summaries.GetValueOrDefault(r.Id)))
            .Where(c => maxKcal is null || (c.Summary?.Kcal ?? 0) <= maxKcal);

        cards = sort switch
        {
            "kcal" => cards.OrderBy(c => c.Summary?.Kcal ?? decimal.MaxValue),
            "protein" => cards.OrderByDescending(c => c.Summary?.Protein ?? 0),
            "cost" => cards.OrderBy(c => c.Summary?.Cost ?? decimal.MaxValue),
            "time" => cards.OrderBy(c => c.Recipe.CookTimeMinutes),
            _ => cards.OrderBy(c => c.Recipe.Title),
        };

        return View(new RecipeIndexVm { Items = cards.ToList(), Method = method, MaxKcal = maxKcal, Sort = sort, CityName = city?.Name });
    }

    public async Task<IActionResult> Details(int id)
    {
        var recipe = await db.Recipes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        if (recipe is null) return NotFound();

        var city = await cityContext.GetCurrentAsync();
        return View(new RecipeDetailsVm
        {
            Recipe = recipe,
            Summary = await calculator.SummarizeAsync(id, city?.Id ?? 0),
            CityName = city?.Name,
        });
    }

    public async Task<IActionResult> Create()
    {
        await FillProducts();
        return View(new RecipeFormVm { Ingredients = [new IngredientRowVm()] });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(ImageValidator.MaxBytes + 1024 * 1024)] 
    public async Task<IActionResult> Create(RecipeFormVm vm)
    {
        await ValidateForm(vm);
        if (!ModelState.IsValid) return await FormView(vm);

        var recipe = new Recipe { Slug = await DomainRules.UniqueRecipeSlugAsync(db, vm.Title, null) };
        Apply(vm, recipe);
        if (vm.Photo is { Length: > 0 })
            recipe.PhotoPath = await files.SaveImageAsync(vm.Photo, PhotoFolder);

        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();
        await cache.RecipeChangedAsync();

        TempData["Success"] = $"Рецепт «{recipe.Title}» додано";
        return RedirectToAction(nameof(Details), new { id = recipe.Id });
    }

    public async Task<IActionResult> Edit(int id)
    {
        var r = await db.Recipes.AsNoTracking().Include(x => x.Ingredients).FirstOrDefaultAsync(x => x.Id == id);
        if (r is null) return NotFound();

        await FillProducts();
        return View(new RecipeFormVm
        {
            Id = r.Id, Title = r.Title, Description = r.Description, StepsMarkdown = r.StepsMarkdown,
            Method = r.Method, CookTimeMinutes = r.CookTimeMinutes, Servings = r.Servings,
            CurrentPhotoPath = r.PhotoPath,
            Ingredients = r.Ingredients.Select(i => new IngredientRowVm { ProductId = i.ProductId, Grams = i.Grams }).ToList(),
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(ImageValidator.MaxBytes + 1024 * 1024)]
    public async Task<IActionResult> Edit(int id, RecipeFormVm vm)
    {
        if (id != vm.Id) return BadRequest();

        var recipe = await db.Recipes.Include(r => r.Ingredients).FirstOrDefaultAsync(r => r.Id == id);
        if (recipe is null) return NotFound();

        await ValidateForm(vm);
        if (!ModelState.IsValid)
        {
            vm.CurrentPhotoPath = recipe.PhotoPath;
            return await FormView(vm);
        }

        if (!string.Equals(recipe.Title, vm.Title.Trim(), StringComparison.Ordinal))
            recipe.Slug = await DomainRules.UniqueRecipeSlugAsync(db, vm.Title, recipe.Id);

        Apply(vm, recipe);

        var oldPhoto = recipe.PhotoPath;
        if (vm.Photo is { Length: > 0 })
            recipe.PhotoPath = await files.SaveImageAsync(vm.Photo, PhotoFolder);
        else if (vm.RemovePhoto)
            recipe.PhotoPath = null;

        await db.SaveChangesAsync();
        await cache.RecipeChangedAsync();

        if (oldPhoto != recipe.PhotoPath) await files.DeleteAsync(oldPhoto);

        TempData["Success"] = "Зміни збережено";
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Delete(int id)
    {
        var recipe = await db.Recipes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        return recipe is null ? NotFound() : View(recipe);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var recipe = await db.Recipes.FindAsync(id);
        if (recipe is null) return NotFound();

        db.Recipes.Remove(recipe); 
        await db.SaveChangesAsync();
        await cache.RecipeChangedAsync();
        await files.DeleteAsync(recipe.PhotoPath);

        TempData["Success"] = $"Рецепт «{recipe.Title}» видалено";
        return RedirectToAction(nameof(Index));
    }

    private async Task ValidateForm(RecipeFormVm vm)
    {
        if (await ImageValidator.ValidateAsync(vm.Photo) is { } photoError)
            ModelState.AddModelError(nameof(vm.Photo), photoError);

        var ids = vm.Ingredients.Select(i => i.ProductId).Where(pid => pid > 0).Distinct().ToList();
        var existing = await db.Products.CountAsync(p => ids.Contains(p.Id));
        if (existing != ids.Count)
            ModelState.AddModelError(nameof(vm.Ingredients), "Один із продуктів не знайдено — оновіть сторінку");
    }

    private static void Apply(RecipeFormVm vm, Recipe r)
    {
        r.Title = vm.Title.Trim();
        r.Description = string.IsNullOrWhiteSpace(vm.Description) ? null : vm.Description.Trim();
        r.StepsMarkdown = string.IsNullOrWhiteSpace(vm.StepsMarkdown) ? null : vm.StepsMarkdown.Trim();
        r.Method = vm.Method;
        r.CookTimeMinutes = vm.CookTimeMinutes;
        r.Servings = vm.Servings;
        DomainRules.SyncIngredients(r, vm.Ingredients.Select(i => (i.ProductId, i.Grams)));
    }

    private async Task<IActionResult> FormView(RecipeFormVm vm)
    {
        if (vm.Ingredients.Count == 0) vm.Ingredients.Add(new IngredientRowVm());
        await FillProducts();
        return View(vm);
    }
    private async Task FillProducts() =>
        ViewBag.Products = new SelectList(
            await db.Products.AsNoTracking().OrderBy(p => p.Name).Select(p => new { p.Id, p.Name }).ToListAsync(),
            "Id", "Name");
}

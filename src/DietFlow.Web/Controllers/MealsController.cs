using DietFlow.Web.Data;
using DietFlow.Web.Models.Entities;
using DietFlow.Web.Models.ViewModels;
using DietFlow.Web.Services;
using DietFlow.Web.Services.Ai;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DietFlow.Web.Controllers;

public class MealsController(
    AppDbContext db, CurrentUser currentUser, IFileStorage files,
    MealAnalysisService analysis, RecipeCalculator calculator, CityContext cityContext) : Controller
{
    private const string PhotoFolder = "meals";
    private const int DaysPerPage = 7;

    public async Task<IActionResult> Index(int page = 1)
    {
        var userId = await currentUser.GetIdAsync();
        var goal = await db.Users.Where(u => u.Id == userId).Select(u => u.DailyKcalGoal).FirstAsync();

        page = Math.Max(1, page);
        var todayLocal = DateOnly.FromDateTime(AppTime.NowLocal);
        var toLocal = todayLocal.AddDays(-(page - 1) * DaysPerPage);
        var fromLocal = toLocal.AddDays(-(DaysPerPage - 1));

        var fromUtc = AppTime.ToUtc(fromLocal.ToDateTime(TimeOnly.MinValue));
        var toUtc = AppTime.ToUtc(toLocal.AddDays(1).ToDateTime(TimeOnly.MinValue));

        var meals = await db.Meals.AsNoTracking()
            .Where(m => m.UserId == userId && m.EatenAt >= fromUtc && m.EatenAt < toUtc)
            .OrderByDescending(m => m.EatenAt)
            .ToListAsync();

        var days = meals
            .GroupBy(m => DateOnly.FromDateTime(AppTime.ToLocal(m.EatenAt)))
            .OrderByDescending(g => g.Key)
            .Select(g => new DiaryDayVm(g.Key, g.OrderBy(m => m.EatenAt).ToList(), g.Sum(m => m.Kcal ?? 0), goal))
            .ToList();

        ViewBag.Page = page;
        ViewBag.HasOlder = await db.Meals.AnyAsync(m => m.UserId == userId && m.EatenAt < fromUtc);
        return View(days);
    }

    [HttpGet]
    public async Task<IActionResult> WeekStats()
    {
        var userId = await currentUser.GetIdAsync();
        var goal = await db.Users.Where(u => u.Id == userId).Select(u => u.DailyKcalGoal).FirstAsync();
        var today = DateOnly.FromDateTime(AppTime.NowLocal);
        var fromUtc = AppTime.ToUtc(today.AddDays(-6).ToDateTime(TimeOnly.MinValue));

        var meals = await db.Meals.AsNoTracking()
            .Where(m => m.UserId == userId && m.EatenAt >= fromUtc)
            .Select(m => new { m.EatenAt, m.Kcal, m.HealthScore })
            .ToListAsync();

        var days = Enumerable.Range(0, 7).Select(i => today.AddDays(i - 6)).Select(day =>
        {
            var ofDay = meals.Where(m => DateOnly.FromDateTime(AppTime.ToLocal(m.EatenAt)) == day).ToList();
            var scores = ofDay.Where(m => m.HealthScore is not null).Select(m => m.HealthScore!.Value).ToList();
            return new
            {
                label = day.ToString("ddd dd.MM"),
                kcal = ofDay.Sum(m => m.Kcal ?? 0),
                avgScore = scores.Count > 0 ? Math.Round(scores.Average(), 1) : (double?)null,
            };
        });
        return Json(new { goal, days });
    }

    public async Task<IActionResult> Details(int id)
    {
        var userId = await currentUser.GetIdAsync();
        var meal = await db.Meals.AsNoTracking()
            .Include(m => m.Recommendations).ThenInclude(r => r.Recipe)
            .FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);
        if (meal is null) return NotFound();

        var city = await cityContext.GetCurrentAsync();
        var summaries = await calculator.SummarizeManyAsync(meal.Recommendations.Select(r => r.RecipeId).ToList(), city?.Id ?? 0);

        return View(new MealDetailsVm
        {
            Meal = meal,
            Analysis = MealAnalysisService.ReadStored(meal.AnalysisJson),
            Recommendations = meal.Recommendations
                .Select(r => new RecommendationVm(r.Recipe, r.Reason, summaries.GetValueOrDefault(r.RecipeId)))
                .ToList(),
            AiAvailable = analysis.IsAvailable,
            CityName = city?.Name,
        });
    }

    public IActionResult Create()
    {
        ViewBag.AiAvailable = analysis.IsAvailable;
        var now = AppTime.NowLocal;
        return View(new MealFormVm
        {
            Type = now.Hour switch { < 11 => MealType.Breakfast, < 16 => MealType.Lunch, < 21 => MealType.Dinner, _ => MealType.Snack },
            EatenAtLocal = now.AddSeconds(-now.Second).AddMilliseconds(-now.Millisecond),
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(ImageValidator.MaxBytes + 1024 * 1024)]
    public async Task<IActionResult> Create(MealFormVm vm, bool analyze = false)
    {
        if (await ImageValidator.ValidateAsync(vm.Photo) is { } photoError)
            ModelState.AddModelError(nameof(vm.Photo), photoError);
        if (analyze && vm.Photo is not { Length: > 0 })
            ModelState.AddModelError(nameof(vm.Photo), "Для AI-аналізу додайте фото страви");
        if (!ModelState.IsValid)
        {
            ViewBag.AiAvailable = analysis.IsAvailable;
            return View(vm);
        }

        var meal = new Meal { UserId = await currentUser.GetIdAsync() };
        Apply(vm, meal);
        if (vm.Photo is { Length: > 0 })
            meal.PhotoPath = await files.SaveImageAsync(vm.Photo, PhotoFolder);

        db.Meals.Add(meal);
        await db.SaveChangesAsync();

        if (analyze)
            await RunAnalysis(meal.Id, "Прийом їжі додано й проаналізовано");
        else
            TempData["Success"] = "Прийом їжі додано";

        return RedirectToAction(nameof(Details), new { id = meal.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Analyze(int id)
    {
        await RunAnalysis(id, "Аналіз оновлено");
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task RunAnalysis(int mealId, string successMessage)
    {
        try
        {
            var city = await cityContext.GetCurrentAsync();
            await analysis.AnalyzeMealAsync(mealId, await currentUser.GetIdAsync(), city?.Id ?? 0, HttpContext.RequestAborted);
            TempData["Success"] = successMessage;
        }
        catch (MealAnalysisException ex)
        {
            TempData["Error"] = ex.Message;
        }
    }

    public async Task<IActionResult> Edit(int id)
    {
        var m = await FindOwned(id);
        if (m is null) return NotFound();

        return View(new MealFormVm
        {
            Id = m.Id, Type = m.Type, EatenAtLocal = AppTime.ToLocal(m.EatenAt), CurrentPhotoPath = m.PhotoPath,
            DishName = m.DishName, Kcal = m.Kcal, ProteinG = m.ProteinG, FatG = m.FatG, CarbsG = m.CarbsG,
            HealthScore = m.HealthScore, Verdict = m.Verdict,
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(ImageValidator.MaxBytes + 1024 * 1024)]
    public async Task<IActionResult> Edit(int id, MealFormVm vm)
    {
        if (id != vm.Id) return BadRequest();
        var meal = await FindOwned(id, tracking: true);
        if (meal is null) return NotFound();

        if (await ImageValidator.ValidateAsync(vm.Photo) is { } photoError)
            ModelState.AddModelError(nameof(vm.Photo), photoError);
        if (!ModelState.IsValid)
        {
            vm.CurrentPhotoPath = meal.PhotoPath;
            return View(vm);
        }

        Apply(vm, meal);
        var oldPhoto = meal.PhotoPath;
        if (vm.Photo is { Length: > 0 })
            meal.PhotoPath = await files.SaveImageAsync(vm.Photo, PhotoFolder);
        else if (vm.RemovePhoto)
            meal.PhotoPath = null;

        await db.SaveChangesAsync();
        if (oldPhoto != meal.PhotoPath) await files.DeleteAsync(oldPhoto);

        TempData["Success"] = "Зміни збережено";
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Delete(int id)
    {
        var meal = await FindOwned(id);
        return meal is null ? NotFound() : View(meal);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var meal = await FindOwned(id, tracking: true);
        if (meal is null) return NotFound();

        db.Meals.Remove(meal);
        await db.SaveChangesAsync();
        await files.DeleteAsync(meal.PhotoPath);

        TempData["Success"] = "Прийом їжі видалено";
        return RedirectToAction(nameof(Index));
    }
    
    private async Task<Meal?> FindOwned(int id, bool tracking = false)
    {
        var userId = await currentUser.GetIdAsync();
        var q = tracking ? db.Meals : db.Meals.AsNoTracking();
        return await q.FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);
    }

    private static void Apply(MealFormVm vm, Meal m)
    {
        m.Type = vm.Type;
        m.EatenAt = AppTime.ToUtc(vm.EatenAtLocal);
        m.DishName = string.IsNullOrWhiteSpace(vm.DishName) ? null : vm.DishName.Trim();
        m.Kcal = vm.Kcal;
        m.ProteinG = vm.ProteinG;
        m.FatG = vm.FatG;
        m.CarbsG = vm.CarbsG;
        m.HealthScore = vm.HealthScore;
        m.Verdict = string.IsNullOrWhiteSpace(vm.Verdict) ? null : vm.Verdict.Trim();
    }
}

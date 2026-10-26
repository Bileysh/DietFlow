using DietFlow.Web.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using DietFlow.Web.Data;
using DietFlow.Web.Models.Api;
using DietFlow.Web.Models.Entities;
using DietFlow.Web.Services;
using DietFlow.Web.Services.Ai;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DietFlow.Web.Controllers.Api;


[Route("api/v1/meals")]
[Tags("Meals (diary)")]
[Authorize]
public class MealsApiController(
    AppDbContext db, CurrentUser currentUser, IFileStorage files, MealAnalysisService analysis) : ApiControllerBase
{
    private const string PhotoFolder = "meals";

    [HttpGet]
    public async Task<PagedResult<MealDto>> List(DateTime? from, DateTime? to, int? skip = null, int? limit = null)
    {
        var (s, l) = Normalize(skip, limit);
        var userId = await currentUser.GetIdAsync();
        var query = Owned(userId);
        if (from is not null) { var fromUtc = ToUtc(from.Value); query = query.Where(m => m.EatenAt >= fromUtc); }
        if (to is not null) { var toUtc = ToUtc(to.Value); query = query.Where(m => m.EatenAt < toUtc); }

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(m => m.EatenAt).ThenByDescending(m => m.Id)
            .Skip(s).Take(l).Select(ToDto).ToListAsync();
        return Page(items, total, s, l);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<MealDto>> Get(int id)
    {
        var meal = await Owned(await currentUser.GetIdAsync()).Where(m => m.Id == id).Select(ToDto).FirstOrDefaultAsync();
        return meal is null ? NotFound() : meal;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<MealDto>> Create(MealRequest request)
    {
        var meal = new Meal { UserId = await currentUser.GetIdAsync() };
        Apply(request, meal);
        db.Meals.Add(meal);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = meal.Id }, await Reload(meal.Id));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<MealDto>> Update(int id, MealRequest request)
    {
        var meal = await FindOwned(id);
        if (meal is null) return NotFound();
        Apply(request, meal);
        await db.SaveChangesAsync();
        return await Reload(id);
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id)
    {
        var meal = await FindOwned(id);
        if (meal is null) return NotFound();
        db.Meals.Remove(meal);
        await db.SaveChangesAsync();
        await files.DeleteAsync(meal.PhotoPath);
        return NoContent();
    }

    [HttpPost("{id:int}/photo")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(ImageValidator.MaxBytes + 1024 * 1024)]
    public async Task<ActionResult<MealDto>> UploadPhoto(int id, IFormFile photo)
    {
        var meal = await FindOwned(id);
        if (meal is null) return NotFound();
        if (await ImageValidator.ValidateAsync(photo) is { } error) return FieldError("photo", error);

        var old = meal.PhotoPath;
        meal.PhotoPath = await files.SaveImageAsync(photo, PhotoFolder);
        await db.SaveChangesAsync();
        await files.DeleteAsync(old);
        return await Reload(id);
    }
    
    [HttpPost("{id:int}/analyze")]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<MealDto>> Analyze(int id, int? cityId)
    {
        var userId = await currentUser.GetIdAsync();
        if (!await Owned(userId).AnyAsync(m => m.Id == id)) return NotFound();
        if (!analysis.IsAvailable)
            return Problem(detail: "AI provider is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);

        try
        {
            await analysis.AnalyzeMealAsync(id, userId, cityId ?? 0, HttpContext.RequestAborted);
        }
        catch (AnalysisLimitException ex)
        {
            return Problem(detail: ex.Message, title: "Daily AI analysis limit reached", statusCode: StatusCodes.Status429TooManyRequests);
        }
        catch (MealAnalysisException ex)
        {
            return Problem(detail: ex.Message, title: "Meal analysis failed",
                statusCode: ex.CanRetry ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status422UnprocessableEntity);
        }
        return await Reload(id);
    }

    private IQueryable<Meal> Owned(string userId) => db.Meals.AsNoTracking().Where(m => m.UserId == userId);

    private async Task<Meal?> FindOwned(int id)
    {
        var userId = await currentUser.GetIdAsync();
        return await db.Meals.FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);
    }

    private async Task<MealDto> Reload(int id) => await db.Meals.AsNoTracking().Where(m => m.Id == id).Select(ToDto).FirstAsync();

    private static readonly System.Linq.Expressions.Expression<Func<Meal, MealDto>> ToDto = m => new MealDto(
        m.Id, m.Type, m.EatenAt, m.PhotoPath, m.DishName, m.Kcal, m.ProteinG, m.FatG, m.CarbsG, m.HealthScore, m.Verdict,
        m.AnalysisJson != null,
        m.Recommendations.Select(r => new MealRecommendationDto(r.RecipeId, r.Recipe.Title, r.Reason)).ToList());

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static void Apply(MealRequest r, Meal m)
    {
        m.Type = r.Type;
        m.EatenAt = r.EatenAt is { } at ? ToUtc(at) : (m.Id == 0 ? DateTime.UtcNow : m.EatenAt);
        m.DishName = string.IsNullOrWhiteSpace(r.DishName) ? null : r.DishName.Trim();
        m.Kcal = r.Kcal;
        m.ProteinG = r.ProteinG;
        m.FatG = r.FatG;
        m.CarbsG = r.CarbsG;
        m.HealthScore = r.HealthScore;
        m.Verdict = string.IsNullOrWhiteSpace(r.Verdict) ? null : r.Verdict.Trim();
    }
}

using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using DietFlow.Web.Data;
using DietFlow.Web.Services.Agent;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace DietFlow.Web.Services.Mcp;

[McpServerToolType]
public class DietFlowMcpTools(AgentTools tools, AppDbContext db, IHttpContextAccessor http, CityContext cityContext, AppEvents events)
{
    [McpServerTool(Name = "search_recipes", Title = "Пошук рецептів", ReadOnly = true, OpenWorld = false)]
    [Description("Пошук рецептів DietFlow з калоріями, БЖВ і вартістю порції за найнижчими цінами в місті. Усі фільтри необов'язкові.")]
    public Task<string> SearchRecipes(
        [Description("Інгредієнт або частина назви, напр. «гречка»")] string? query = null,
        [Description("Максимум ккал на порцію")] decimal? maxKcal = null,
        [Description("Максимальна вартість порції, грн")] decimal? maxCost = null,
        [Description("Мінімум білка на порцію, г")] decimal? minProtein = null,
        [Description("Сортування: cost, kcal або protein")] string? sort = null,
        [Description("Скільки рецептів (1–10)")] int? limit = null,
        [Description("Місто для цін, напр. «Львів»; за замовчуванням — Київ")] string? city = null,
        CancellationToken ct = default) =>
        RunAsync("search_recipes", Args(("query", query), ("maxKcal", maxKcal), ("maxCost", maxCost), ("minProtein", minProtein),
            ("sort", sort), ("limit", limit)), city, ct);

    [McpServerTool(Name = "get_recipe", Title = "Рецепт", ReadOnly = true, OpenWorld = false)]
    [Description("Деталі рецепта: інгредієнти, ккал/БЖВ і вартість порції, найдешевший магазин для кожного інгредієнта.")]
    public Task<string> GetRecipe(
        [Description("Id рецепта з search_recipes")] int recipeId,
        [Description("Місто для цін; за замовчуванням — Київ")] string? city = null,
        CancellationToken ct = default) =>
        RunAsync("get_recipe", Args(("recipeId", recipeId)), city, ct);

    [McpServerTool(Name = "get_diary", Title = "Щоденник за день", ReadOnly = true, OpenWorld = false)]
    [Description("Щоденник харчування власника токена за день: прийоми їжі, сума ккал і БЖВ, денна норма і залишок.")]
    public Task<string> GetDiary(
        [Description("Дата YYYY-MM-DD; за замовчуванням — сьогодні (Київ)")] string? date = null,
        CancellationToken ct = default) =>
        RunAsync("get_diary", Args(("date", date)), null, ct);

    [McpServerTool(Name = "find_cheapest_stores", Title = "Найдешевші магазини", ReadOnly = true, OpenWorld = false)]
    [Description("Найдешевші магазини з продуктом у місті з цінами за кг.")]
    public Task<string> FindCheapestStores(
        [Description("Назва продукту, напр. «куряче філе»")] string product,
        [Description("Скільки магазинів (1–10)")] int? limit = null,
        [Description("Місто; за замовчуванням — Київ")] string? city = null,
        CancellationToken ct = default) =>
        RunAsync("find_cheapest_stores", Args(("product", product), ("limit", limit)), city, ct);

    [McpServerTool(Name = "add_meal", Title = "Додати прийом їжі", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Додає прийом їжі в щоденник власника токена. Або recipeId (+portions), або dishName + kcal (+ БЖВ).")]
    public Task<string> AddMeal(
        [Description("Breakfast, Lunch, Dinner або Snack")] string type,
        [Description("Id рецепта, якщо страва з каталогу")] int? recipeId = null,
        [Description("Кількість порцій рецепта")] decimal? portions = null,
        [Description("Назва страви (якщо не з рецепта)")] string? dishName = null,
        [Description("Калорії")] int? kcal = null,
        [Description("Білки, г")] decimal? proteinG = null,
        [Description("Жири, г")] decimal? fatG = null,
        [Description("Вуглеводи, г")] decimal? carbsG = null,
        CancellationToken ct = default) =>
        RunAsync("add_meal", Args(("type", type), ("recipeId", recipeId), ("portions", portions), ("dishName", dishName),
            ("kcal", kcal), ("proteinG", proteinG), ("fatG", fatG), ("carbsG", carbsG)), null, ct);

    private async Task<string> RunAsync(string tool, JsonObject args, string? cityName, CancellationToken ct)
    {
        var userId = http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? throw new UnauthorizedAccessException("Потрібен API-токен DietFlow.");

        var city = cityName is null
            ? await cityContext.GetCurrentAsync()
            : await db.Cities.AsNoTracking().Where(c => EF.Functions.ILike(c.Name, cityName.Trim()))
                  .OrderByDescending(c => c.Stores.Count).FirstOrDefaultAsync(ct)
              ?? throw new McpException($"Місто «{cityName}» не знайдено.");

        events.Track(AppEvents.McpToolCalled, ("tool", tool));
        try
        {
            var (result, _) = await tools.ExecuteAsync(tool, args, new AgentContext(userId, city?.Id ?? 0, city?.Name, null, null, null), ct);
            return result.ToJsonString();
        }
        catch (AgentException ex)
        {
            throw new McpException(ex.Message);
        }
    }

    private static JsonObject Args(params (string Key, object? Value)[] pairs)
    {
        var args = new JsonObject();
        foreach (var (key, value) in pairs)
            if (value is not null) args[key] = JsonSerializer.SerializeToNode(value);
        return args;
    }
}

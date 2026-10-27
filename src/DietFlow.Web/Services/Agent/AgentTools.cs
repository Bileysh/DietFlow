using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DietFlow.Web.Data;
using DietFlow.Web.Data.Import;
using DietFlow.Web.Models.Entities;
using DietFlow.Web.Models.ViewModels;
using DietFlow.Web.Services.Ai;
using Microsoft.EntityFrameworkCore;

namespace DietFlow.Web.Services.Agent;

public class AgentTools(AppDbContext db, RecipeCalculator calculator, MealAnalysisService analysis, IFileStorage files)
{
    private const int MaxListSize = 10;
    private const double NearbyRadiusKm = 5;

    public static readonly IReadOnlyList<ToolDefinition> Definitions =
    [
        new("search_recipes",
            "Пошук рецептів каталогу DietFlow з калоріями, БЖВ і вартістю порції за найнижчими цінами в місті користувача. " +
            "Усі фільтри необов'язкові.",
            Schema(new()
            {
                ["query"] = Prop("string", "Інгредієнт або частина назви страви, напр. «гречка», «курка». " +
                                           "НЕ передавай «сніданок/обід/вечеря» — таких слів у назвах немає; для прийому їжі достатньо maxKcal"),
                ["maxKcal"] = Prop("number", "Максимум ккал на порцію"),
                ["maxCost"] = Prop("number", "Максимальна вартість порції, грн"),
                ["minProtein"] = Prop("number", "Мінімум білка на порцію, г"),
                ["method"] = Prop("string", "Спосіб приготування", Enum.GetNames<CookingMethod>()),
                ["sort"] = Prop("string", "Сортування", ["cost", "kcal", "protein"]),
                ["limit"] = Prop("integer", "Скільки рецептів повернути (1–10, за замовчуванням 5)"),
            })),
        new("get_recipe",
            "Деталі рецепта: інгредієнти з грамами, ккал/БЖВ і вартість порції, найдешевший магазин для кожного інгредієнта.",
            Schema(new() { ["recipeId"] = Prop("integer", "Id рецепта з search_recipes") }, "recipeId")),
        new("get_diary",
            "Щоденник харчування користувача за день: прийоми їжі, сума ккал і БЖВ, денна норма і скільки ще лишилось.",
            Schema(new() { ["date"] = Prop("string", "Дата у форматі YYYY-MM-DD; за замовчуванням — сьогодні") })),
        new("find_cheapest_stores",
            "Найдешевші магазини з цим продуктом у місті користувача (або поруч із ним, якщо відоме місцезнаходження).",
            Schema(new()
            {
                ["product"] = Prop("string", "Назва продукту, напр. «гречка», «куряче філе»"),
                ["limit"] = Prop("integer", "Скільки магазинів (1–10, за замовчуванням 5)"),
            }, "product")),
        new("analyze_photo",
            "AI-аналіз фото страви, яке користувач прикріпив до повідомлення: назва, ккал, БЖВ, оцінка корисності, поради. " +
            "Нічого не записує в щоденник.",
            Schema(new())),
        new("add_meal",
            "Додає прийом їжі в щоденник користувача. Викликай ЛИШЕ коли користувач прямо попросив додати/записати. " +
            "Джерело даних — одне з: usePhotoAnalysis=true (після analyze_photo), recipeId (+portions) або dishName+kcal.",
            Schema(new()
            {
                ["type"] = Prop("string", "Прийом їжі", Enum.GetNames<MealType>()),
                ["usePhotoAnalysis"] = Prop("boolean", "Взяти дані з результату analyze_photo і прикріпити фото"),
                ["recipeId"] = Prop("integer", "Id рецепта, якщо страва з каталогу"),
                ["portions"] = Prop("number", "Кількість порцій рецепта (за замовчуванням 1)"),
                ["dishName"] = Prop("string", "Назва страви (якщо не з рецепта і не з фото)"),
                ["kcal"] = Prop("integer", "Калорії"),
                ["proteinG"] = Prop("number", "Білки, г"),
                ["fatG"] = Prop("number", "Жири, г"),
                ["carbsG"] = Prop("number", "Вуглеводи, г"),
            }, "type")),
    ];

    public static string Label(string tool, JsonObject args) => tool switch
    {
        "search_recipes" => "🔍 Шукаю рецепти" + Filters(args),
        "get_recipe" => $"📖 Відкриваю рецепт #{Int(args, "recipeId")}",
        "get_diary" => $"📒 Дивлюсь щоденник за {Str(args, "date") ?? "сьогодні"}",
        "find_cheapest_stores" => $"💰 Шукаю, де найдешевше: {Str(args, "product")}",
        "analyze_photo" => "📷 Аналізую фото страви",
        "add_meal" => "✍ Додаю в щоденник",
        _ => $"⚙ {tool}",
    };

    public async Task<(JsonNode Result, string Summary)> ExecuteAsync(string tool, JsonObject args, AgentContext ctx, CancellationToken ct)
    {
        return tool switch
        {
            "search_recipes" => await SearchRecipesAsync(args, ctx, ct),
            "get_recipe" => await GetRecipeAsync(args, ctx, ct),
            "get_diary" => await GetDiaryAsync(args, ctx, ct),
            "find_cheapest_stores" => await FindCheapestStoresAsync(args, ctx, ct),
            "analyze_photo" => await AnalyzePhotoAsync(ctx, ct),
            "add_meal" => await AddMealAsync(args, ctx, ct),
            _ => throw new AgentException($"Невідомий інструмент «{tool}»."),
        };
    }

    private async Task<(JsonNode, string)> SearchRecipesAsync(JsonObject args, AgentContext ctx, CancellationToken ct)
    {
        var query = Str(args, "query");
        var methodName = Str(args, "method");
        var recipes = await db.Recipes.AsNoTracking()
            .Where(r => query == null || EF.Functions.ILike(r.Title, "%" + query + "%"))
            .Select(r => new { r.Id, r.Title, r.Slug, r.Method, r.CookTimeMinutes })
            .ToListAsync(ct);
        if (Enum.TryParse<CookingMethod>(methodName, true, out var method))
            recipes = recipes.Where(r => r.Method == method).ToList();

        var summaries = await calculator.SummarizeManyAsync(recipes.Select(r => r.Id).ToList(), ctx.CityId);
        var maxKcal = Dec(args, "maxKcal");
        var maxCost = Dec(args, "maxCost");
        var minProtein = Dec(args, "minProtein");

        var rows = recipes
            .Where(r => summaries.ContainsKey(r.Id))
            .Select(r => (Recipe: r, S: summaries[r.Id]))
            .Where(x => maxKcal is null || x.S.Kcal <= maxKcal)
            .Where(x => maxCost is null || x.S.Cost is { } c && c <= maxCost)
            .Where(x => minProtein is null || x.S.Protein >= minProtein);
        rows = Str(args, "sort") switch
        {
            "kcal" => rows.OrderBy(x => x.S.Kcal),
            "protein" => rows.OrderByDescending(x => x.S.Protein),
            _ => rows.OrderBy(x => x.S.Cost ?? decimal.MaxValue),
        };
        var list = rows.Take(Limit(args, 5)).Select(x => new
        {
            id = x.Recipe.Id, title = x.Recipe.Title, url = $"/recipes/{x.Recipe.Slug}",
            method = CookingMethodText.Name(x.Recipe.Method), cookMinutes = x.Recipe.CookTimeMinutes,
            kcal = x.S.Kcal, protein = x.S.Protein, fat = x.S.Fat, carbs = x.S.Carbs,
            costUah = x.S.Cost, costComplete = x.S.CostIsComplete,
        }).ToList();

        var hint = list.Count == 0 && query is not null ? "За цією назвою нічого — спробуй без query, лише з числовими фільтрами." : null;
        return (Node(new { city = ctx.CityName, count = list.Count, recipes = list, hint }),
            list.Count == 0 ? "нічого не знайдено" : $"знайдено {list.Count} {Plural(list.Count, "рецепт", "рецепти", "рецептів")}");
    }

    private async Task<(JsonNode, string)> GetRecipeAsync(JsonObject args, AgentContext ctx, CancellationToken ct)
    {
        var id = Int(args, "recipeId") ?? throw new AgentException("Не вказано recipeId.");
        var recipe = await db.Recipes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct)
                     ?? throw new AgentException($"Рецепт #{id} не знайдено.");
        var s = await calculator.SummarizeAsync(id, ctx.CityId);

        return (Node(new
        {
            id, title = recipe.Title, url = $"/recipes/{recipe.Slug}", description = recipe.Description,
            method = CookingMethodText.Name(recipe.Method), cookMinutes = recipe.CookTimeMinutes, servings = recipe.Servings,
            perServing = s is null ? null : new { kcal = s.Kcal, protein = s.Protein, fat = s.Fat, carbs = s.Carbs, costUah = s.Cost },
            ingredients = s?.Lines.Select(l => new
            {
                product = l.ProductName, grams = l.Grams, pricePerKg = l.PricePerKg, cheapestStore = l.CheapestStore, costUah = l.Cost,
            }),
            missingPrices = s?.MissingPrices,
            city = ctx.CityName,
        }), recipe.Title);
    }

    private async Task<(JsonNode, string)> GetDiaryAsync(JsonObject args, AgentContext ctx, CancellationToken ct)
    {
        var date = DateOnly.TryParseExact(Str(args, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : DateOnly.FromDateTime(AppTime.NowLocal);
        var from = AppTime.ToUtc(date.ToDateTime(TimeOnly.MinValue));
        var to = from.AddDays(1);

        var goal = await db.Users.Where(u => u.Id == ctx.UserId).Select(u => u.DailyKcalGoal).FirstAsync(ct);
        var meals = await db.Meals.AsNoTracking()
            .Where(m => m.UserId == ctx.UserId && m.EatenAt >= from && m.EatenAt < to)
            .OrderBy(m => m.EatenAt)
            .Select(m => new { m.Id, m.Type, m.EatenAt, m.DishName, m.Kcal, m.ProteinG, m.FatG, m.CarbsG, m.HealthScore })
            .ToListAsync(ct);

        var kcal = meals.Sum(m => m.Kcal ?? 0);
        return (Node(new
        {
            date = date.ToString("yyyy-MM-dd"),
            meals = meals.Select(m => new
            {
                m.Id, type = m.Type.ToString(), time = AppTime.ToLocal(m.EatenAt).ToString("HH:mm"),
                dish = m.DishName, m.Kcal, m.ProteinG, m.FatG, m.CarbsG, m.HealthScore,
            }),
            totals = new { kcal, protein = meals.Sum(m => m.ProteinG ?? 0), fat = meals.Sum(m => m.FatG ?? 0), carbs = meals.Sum(m => m.CarbsG ?? 0) },
            dailyGoalKcal = goal,
            remainingKcal = goal - kcal,
        }), meals.Count == 0 ? "записів немає" : $"{meals.Count} {Plural(meals.Count, "запис", "записи", "записів")}, {kcal} ккал з {goal}");
    }

    private async Task<(JsonNode, string)> FindCheapestStoresAsync(JsonObject args, AgentContext ctx, CancellationToken ct)
    {
        var query = Str(args, "product") ?? throw new AgentException("Не вказано продукт.");
        var nearby = ctx.Lat is not null && ctx.Lng is not null;

        var product = await db.Products.AsNoTracking()
            .Where(p => EF.Functions.ILike(p.Name, "%" + query + "%") && p.Prices.Any())
            .OrderByDescending(p => p.Prices.Count)
            .Select(p => new { p.Id, p.Name })
            .FirstOrDefaultAsync(ct);
        if (product is null) return (Node(new { product = query, found = false }), "продукт без цін");

        var prices = db.StorePrices.AsNoTracking().Where(sp => sp.ProductId == product.Id);
        if (nearby)
        {
            var dLat = NearbyRadiusKm / 111.0;
            var dLng = NearbyRadiusKm / (111.0 * Math.Cos(ctx.Lat!.Value * Math.PI / 180));
            prices = prices.Where(sp => sp.Store.Latitude >= ctx.Lat - dLat && sp.Store.Latitude <= ctx.Lat + dLat
                                        && sp.Store.Longitude >= ctx.Lng - dLng && sp.Store.Longitude <= ctx.Lng + dLng);
        }
        else prices = prices.Where(sp => sp.Store.CityId == ctx.CityId);

        var rows = await prices.OrderBy(sp => sp.PricePerKg).Take(50)
            .Select(sp => new { sp.StoreId, sp.Store.Name, sp.Store.Chain, sp.Store.Address, sp.Store.Latitude, sp.Store.Longitude, sp.PricePerKg, sp.Source })
            .ToListAsync(ct);
        var stores = rows
            .Select(r => new
            {
                storeId = r.StoreId, name = r.Name, chain = r.Chain, address = r.Address, pricePerKg = r.PricePerKg,
                priceSource = r.Source.ToString(), url = $"/Stores/Details/{r.StoreId}",
                distanceKm = nearby ? Math.Round(OsmStoresImporter.DistanceKm(ctx.Lat!.Value, ctx.Lng!.Value, r.Latitude, r.Longitude), 1) : (double?)null,
            })
            .Where(s => !nearby || s.distanceKm <= NearbyRadiusKm)
            .Take(Limit(args, 5)).ToList();

        return (Node(new
        {
            product = product.Name, area = nearby ? $"у радіусі {NearbyRadiusKm} км від користувача" : ctx.CityName,
            priceNote = "Stat — оцінка за середньою ціною Держстату по області з поправкою на мережу; Manual — перевірено адміністратором; Demo — демо.",
            stores,
        }), stores.Count == 0 ? "магазинів не знайдено" : $"{product.Name}: від {stores[0].pricePerKg:0.00} грн/кг ({stores[0].chain})");
    }

    private async Task<(JsonNode, string)> AnalyzePhotoAsync(AgentContext ctx, CancellationToken ct)
    {
        if (ctx.Image is null) throw new AgentException("До повідомлення не прикріплено фото.");
        var a = await analysis.AnalyzeImageAsync(ctx.Image, ctx.UserId, ctx.CityId, ct);
        ctx.LastPhotoAnalysis = a;

        return (Node(new
        {
            a.IsFood, a.DishName, a.Confidence, a.Kcal, a.ProteinG, a.FatG, a.CarbsG, a.HealthScore, a.Verdict,
            ingredients = a.Ingredients, a.Issues, a.Improvements,
            recommendedRecipeIds = a.RecommendedRecipes.Select(r => new { r.RecipeId, r.Reason }),
        }), a.IsFood ? $"{a.DishName}: ~{a.Kcal} ккал" : "на фото не їжа");
    }

    private async Task<(JsonNode, string)> AddMealAsync(JsonObject args, AgentContext ctx, CancellationToken ct)
    {
        if (!Enum.TryParse<MealType>(Str(args, "type"), true, out var type))
            throw new AgentException("Вкажіть тип прийому їжі: Breakfast, Lunch, Dinner або Snack.");

        var meal = new Meal { UserId = ctx.UserId, Type = type, EatenAt = DateTime.UtcNow };

        if (Bool(args, "usePhotoAnalysis") == true)
        {
            var a = ctx.LastPhotoAnalysis ?? throw new AgentException("Спершу проаналізуйте фото (analyze_photo).");
            if (!a.IsFood) throw new AgentException("На фото не їжа — додавати нічого.");
            meal.PhotoPath = await files.SaveImageAsync(ToFormFile(ctx.Image!), "meals", ct);
            await analysis.ApplyAsync(meal, a, ct);
        }
        else if (Int(args, "recipeId") is { } recipeId)
        {
            var title = await db.Recipes.Where(r => r.Id == recipeId).Select(r => r.Title).FirstOrDefaultAsync(ct)
                        ?? throw new AgentException($"Рецепт #{recipeId} не знайдено.");
            var s = await calculator.SummarizeAsync(recipeId, ctx.CityId) ?? throw new AgentException("Для рецепта немає інгредієнтів.");
            var portions = Math.Clamp(Dec(args, "portions") ?? 1, 0.25m, 5);
            meal.DishName = portions == 1 ? title : $"{title} ({portions:0.##} порц.)";
            meal.Kcal = (int)Math.Round(s.Kcal * portions);
            meal.ProteinG = Math.Round(s.Protein * portions, 1);
            meal.FatG = Math.Round(s.Fat * portions, 1);
            meal.CarbsG = Math.Round(s.Carbs * portions, 1);
        }
        else
        {
            meal.DishName = Trim(Str(args, "dishName"), 200) ?? throw new AgentException("Вкажіть назву страви.");
            meal.Kcal = Int(args, "kcal") is { } k ? Math.Clamp(k, 0, 10000) : null;
            meal.ProteinG = Clamp(Dec(args, "proteinG"));
            meal.FatG = Clamp(Dec(args, "fatG"));
            meal.CarbsG = Clamp(Dec(args, "carbsG"));
        }

        db.Meals.Add(meal);
        await db.SaveChangesAsync(ct);
        return (Node(new { added = true, mealId = meal.Id, url = $"/Meals/Details/{meal.Id}", meal.DishName, meal.Kcal, type = type.ToString() }),
            $"додано: {meal.DishName}{(meal.Kcal is { } kc ? $", {kc} ккал" : "")}");
    }

    private static JsonObject Schema(Dictionary<string, JsonObject> props, params string[] required)
    {
        var schema = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject(props.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value))) };
        if (required.Length > 0) schema["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray());
        return schema;
    }

    private static JsonObject Prop(string type, string description, string[]? values = null)
    {
        var p = new JsonObject { ["type"] = type, ["description"] = description };
        if (values is not null) p["enum"] = new JsonArray(values.Select(v => (JsonNode)v).ToArray());
        return p;
    }

    private static JsonNode Node(object value) => JsonSerializer.SerializeToNode(value, JsonSerializerOptions.Web)!;

    private static string? Str(JsonObject a, string key) =>
        a[key] is JsonValue v && v.ToString() is { Length: > 0 } s ? s.Trim() : null;

    private static decimal? Dec(JsonObject a, string key) =>
        decimal.TryParse(Str(a, key), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static int? Int(JsonObject a, string key) => Dec(a, key) is { } d ? (int)Math.Round(d) : null;

    private static bool? Bool(JsonObject a, string key) => bool.TryParse(Str(a, key), out var b) ? b : null;

    private static int Limit(JsonObject a, int fallback) => Math.Clamp(Int(a, "limit") ?? fallback, 1, MaxListSize);

    private static decimal? Clamp(decimal? v) => v is null ? null : Math.Clamp(v.Value, 0, 1000);

    private static string? Trim(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];

    private static string Filters(JsonObject a)
    {
        var parts = new List<string>();
        if (Str(a, "query") is { } q) parts.Add($"«{q}»");
        if (Dec(a, "maxKcal") is { } k) parts.Add($"до {k:0} ккал");
        if (Dec(a, "maxCost") is { } c) parts.Add($"до {c:0} грн");
        if (Dec(a, "minProtein") is { } p) parts.Add($"від {p:0} г білка");
        return parts.Count == 0 ? "" : $" ({string.Join(", ", parts)})";
    }

    private static string Plural(int n, string one, string few, string many) =>
        (n % 10 == 1 && n % 100 != 11) ? one : (n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14) ? few : many;

    private static IFormFile ToFormFile(byte[] bytes) =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "photo", "photo.jpg")
        {
            Headers = new HeaderDictionary(), ContentType = ImageValidator.DetectMediaType(bytes) ?? "image/jpeg",
        };
}

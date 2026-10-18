using System.Text;
using System.Text.Json;

namespace DietFlow.Web.Services.Ai;

public static class MealAnalysisPrompt
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, 
    };

    public const string UserText = "Проаналізуй страву на фото за інструкцією.";

    public static string BuildSystemPrompt(IReadOnlyList<CatalogRecipe> catalog)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""
            Ти — нутриціолог у застосунку DietFlow. Користувач надсилає фото страви.

            Завдання:
            1. Визнач, чи є на фото їжа. Якщо ні — is_food=false, числові поля 0, списки порожні, у verdict коротко поясни, що на фото.
            2. Назви страву українською та оціни основні інгредієнти з вагою в грамах (оцінка на око — нормально).
            3. Оціни калорійність і БЖВ усієї порції на фото.
            4. Постав health_score від 1 (дуже шкідливо) до 10 (ідеально збалансовано). Враховуй баланс БЖВ,
               спосіб приготування (смаження у великій кількості олії, майонез, цукор — мінус; овочі, нежирний білок, цільні крупи — плюс)
               і розмір порції.
            5. verdict — одне речення-висновок. issues — конкретні проблеми страви (0–4 пункти).
               improvements — практичні поради, як зробити саме цю страву кориснішою (заміни інгредієнтів, спосіб приготування, порція), 0–4 пункти.
            6. recommended_recipes — 0–3 рецепти ТІЛЬКИ з каталогу нижче (recipe_id з каталогу), які є кращою
               альтернативою: схожі за типом прийому їжі чи смаком, але корисніші. Якщо страва вже корисна (score ≥ 8),
               можна повернути порожній список. reason — чому цей рецепт кращий, одним реченням.
            7. confidence: "high" — страву чітко видно; "medium" — частина інгредієнтів неочевидна; "low" — фото нечітке або страва незвична.

            Пиши всі тексти українською, коротко і без медичних діагнозів.

            Каталог рецептів (id | назва | ккал | білок г | жир г | ціна порції грн | спосіб):
            """);
        foreach (var r in catalog.OrderBy(r => r.Id))
            sb.AppendLine($"{r.Id} | {r.Title} | {r.Kcal:0} | {r.Protein:0} | {r.Fat:0} | {(r.Cost is { } c ? c.ToString("0") : "—")} | {r.Method}");
        return sb.ToString();
    }

    public static readonly Dictionary<string, JsonElement> Schema = BuildSchema();

    private static Dictionary<string, JsonElement> BuildSchema()
    {
        object Str(string description) => new { type = "string", description };
        object StrArray(string description) => new { type = "array", items = new { type = "string" }, description };

        var properties = new Dictionary<string, object>
        {
            ["is_food"] = new { type = "boolean" },
            ["dish_name"] = Str("Назва страви українською"),
            ["confidence"] = new { type = "string", @enum = new[] { "high", "medium", "low" } },
            ["ingredients"] = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new { name = new { type = "string" }, grams = new { type = "number" } },
                    required = new[] { "name", "grams" },
                    additionalProperties = false,
                },
            },
            ["kcal"] = new { type = "integer" },
            ["protein_g"] = new { type = "number" },
            ["fat_g"] = new { type = "number" },
            ["carbs_g"] = new { type = "number" },
            ["health_score"] = new { type = "integer", description = "1..10" },
            ["verdict"] = Str("Одне речення-висновок"),
            ["issues"] = StrArray("Проблеми страви"),
            ["improvements"] = StrArray("Як зробити кориснішою"),
            ["recommended_recipes"] = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new { recipe_id = new { type = "integer" }, reason = new { type = "string" } },
                    required = new[] { "recipe_id", "reason" },
                    additionalProperties = false,
                },
            },
        };

        return new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["properties"] = JsonSerializer.SerializeToElement(properties),
            ["required"] = JsonSerializer.SerializeToElement(properties.Keys.ToArray()),
            ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
        };
    }

    public static MealAnalysis Parse(string json, ILogger logger)
    {
        try
        {
            return JsonSerializer.Deserialize<MealAnalysis>(json, Json) ?? throw new JsonException("empty");
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Cannot parse analysis JSON: {Json}", json);
            throw new MealAnalysisException("AI повернув некоректну відповідь. Спробуйте ще раз.", canRetry: true, ex);
        }
    }
}

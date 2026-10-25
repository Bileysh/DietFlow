using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace DietFlow.Web.Services.Ai;

public class GeminiOptions
{
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "gemini-flash-latest";

    public string? FallbackModel { get; set; } = "gemini-flash-lite-latest";
}

public class GeminiMealAnalyzer(HttpClient http, IOptions<GeminiOptions> options, ILogger<GeminiMealAnalyzer> logger) : IMealAnalyzer
{
    private readonly GeminiOptions _opt = options.Value;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_opt.ApiKey);
    public string ProviderName => $"Gemini ({_opt.Model})";

    public async Task<MealAnalysis> AnalyzeAsync(
        byte[] image, string mediaType, IReadOnlyList<CatalogRecipe> catalog, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new MealAnalysisException("AI-аналіз не налаштовано: відсутній ключ Gemini API.", canRetry: false);

        var body = new
        {
            systemInstruction = new { parts = new[] { new { text = MealAnalysisPrompt.BuildSystemPrompt(catalog) } } },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new object[]
                    {
                        new { inlineData = new { mimeType = mediaType, data = Convert.ToBase64String(image) } },
                        new { text = MealAnalysisPrompt.UserText },
                    },
                },
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseJsonSchema = MealAnalysisPrompt.Schema,
            },
        };

        var models = new[] { _opt.Model, _opt.FallbackModel }
            .Where(m => !string.IsNullOrWhiteSpace(m)).Distinct().ToArray();

        for (var i = 0; ; i++)
        {
            var model = models[i]!;
            var isLast = i == models.Length - 1;
            try
            {
                return await SendAsync(model, body, isLast, ct);
            }
            catch (MealAnalysisException ex) when (!isLast && ex.CanRetry)
            {
                logger.LogWarning("Gemini model {Model} failed ({Message}) — retrying with {Fallback}", model, ex.Message, models[i + 1]);
            }
        }
    }

    private async Task<MealAnalysis> SendAsync(string model, object body, bool isLast, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("x-goog-api-key", _opt.ApiKey);

        using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (!isLast) attemptCts.CancelAfter(TimeSpan.FromSeconds(45));

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, attemptCts.Token);
        }
        catch (HttpRequestException ex)
        {
            throw new MealAnalysisException("Немає зв'язку з сервісом AI. Перевірте інтернет.", canRetry: true, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new MealAnalysisException("AI відповідає занадто довго. Спробуйте ще раз.", canRetry: true, ex);
        }

        using (response)
        {
            var raw = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw MapError(model, response.StatusCode, raw);

            var root = JsonNode.Parse(raw);
            LogUsage(model, root);

            if (root?["promptFeedback"]?["blockReason"]?.GetValue<string>() is { } blockReason)
            {
                logger.LogWarning("Gemini blocked prompt: {Reason}", blockReason);
                throw new MealAnalysisException("AI відмовився аналізувати це зображення.", canRetry: false);
            }

            var candidate = root?["candidates"]?[0];
            var finish = candidate?["finishReason"]?.GetValue<string>();
            if (finish is "SAFETY" or "PROHIBITED_CONTENT" or "BLOCKLIST" or "SPII" or "IMAGE_SAFETY")
                throw new MealAnalysisException("AI відмовився аналізувати це зображення.", canRetry: false);
            if (finish == "MAX_TOKENS")
                throw new MealAnalysisException("Відповідь AI обірвалась. Спробуйте ще раз.", canRetry: true);

            var json = string.Concat(
                (candidate?["content"]?["parts"]?.AsArray() ?? [])
                .Where(p => p?["thought"]?.GetValue<bool>() != true)
                .Select(p => p?["text"]?.GetValue<string>() ?? ""));

            if (string.IsNullOrWhiteSpace(json))
                throw new MealAnalysisException("AI повернув порожню відповідь. Спробуйте ще раз.", canRetry: true);

            return MealAnalysisPrompt.Parse(json, logger);
        }
    }

    private MealAnalysisException MapError(string model, HttpStatusCode status, string raw)
    {
        logger.LogError("Gemini API error {Model} {Status}: {Body}", model, (int)status, raw);
        return (int)status switch
        {
            429 => new MealAnalysisException("Вичерпано безкоштовний ліміт запитів до AI. Спробуйте за хвилину або завтра.", canRetry: true),
            400 when raw.Contains("API_KEY_INVALID") => new MealAnalysisException("Ключ Gemini API недійсний — перевірте налаштування.", canRetry: false),
            401 or 403 => new MealAnalysisException("Немає доступу до Gemini API — перевірте ключ і регіон.", canRetry: false),
            404 => new MealAnalysisException($"Модель «{model}» недоступна — змініть Gemini:Model у налаштуваннях.", canRetry: false),
            >= 500 => new MealAnalysisException("Сервіс AI тимчасово недоступний. Спробуйте пізніше.", canRetry: true),
            _ => new MealAnalysisException("AI не зміг обробити фото. Спробуйте інше фото.", canRetry: true),
        };
    }

    private void LogUsage(string model, JsonNode? root)
    {
        var usage = root?["usageMetadata"];
        logger.LogInformation("Meal analysis: model={Model} in={In} out={Out} thoughts={Thoughts}",
            model, usage?["promptTokenCount"], usage?["candidatesTokenCount"], usage?["thoughtsTokenCount"]);
    }
}

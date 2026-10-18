using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;

namespace DietFlow.Web.Services.Ai;

public class AnthropicOptions
{
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "claude-haiku-5-5";
    public string Effort { get; set; } = "medium";
    public int MaxTokens { get; set; } = 8000;
}

public class ClaudeMealAnalyzer(IOptions<AnthropicOptions> options, ILogger<ClaudeMealAnalyzer> logger) : IMealAnalyzer
{
    private readonly AnthropicOptions _opt = options.Value;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_opt.ApiKey);
    public string ProviderName => $"Claude ({_opt.Model})";

    public async Task<MealAnalysis> AnalyzeAsync(
        byte[] image, string mediaType, IReadOnlyList<CatalogRecipe> catalog, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new MealAnalysisException("AI-аналіз не налаштовано: відсутній ключ Anthropic API.", canRetry: false);

        var client = new AnthropicClient { ApiKey = _opt.ApiKey };

        var parameters = new MessageCreateParams
        {
            Model = _opt.Model,
            MaxTokens = _opt.MaxTokens,
            System = new List<TextBlockParam>
            {
                new() { Text = MealAnalysisPrompt.BuildSystemPrompt(catalog), CacheControl = new CacheControlEphemeral() },
            },
            Messages =
            [
                new()
                {
                    Role = Role.User,
                    Content = new List<ContentBlockParam>
                    {
                        new ImageBlockParam
                        {
                            Source = new Base64ImageSource { Data = Convert.ToBase64String(image), MediaType = mediaType },
                        },
                        new TextBlockParam { Text = MealAnalysisPrompt.UserText },
                    },
                },
            ],
            OutputConfig = new OutputConfig
            {
                Effort = ParseEffort(_opt.Effort),
                Format = new JsonOutputFormat { Schema = MealAnalysisPrompt.Schema },
            },
        };

        Message response;
        try
        {
            response = await client.Messages.Create(parameters, ct);
        }
        catch (AnthropicRateLimitException ex)
        {
            throw new MealAnalysisException("Забагато запитів до AI. Спробуйте за хвилину.", canRetry: true, ex);
        }
        catch (Anthropic5xxException ex)
        {
            throw new MealAnalysisException("Сервіс AI тимчасово недоступний. Спробуйте пізніше.", canRetry: true, ex);
        }
        catch (AnthropicUnauthorizedException ex)
        {
            logger.LogError(ex, "Anthropic API key rejected");
            throw new MealAnalysisException("Ключ Anthropic API недійсний — перевірте налаштування.", canRetry: false, ex);
        }
        catch (AnthropicApiException ex)
        {
            logger.LogError(ex, "Anthropic API error");
            throw new MealAnalysisException("AI не зміг обробити фото. Спробуйте інше фото.", canRetry: true, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new MealAnalysisException("Немає зв'язку з сервісом AI. Перевірте інтернет.", canRetry: true, ex);
        }

        logger.LogInformation("Meal analysis: model={Model} in={In} out={Out} cacheRead={CacheRead} stop={Stop}",
            _opt.Model, response.Usage.InputTokens, response.Usage.OutputTokens,
            response.Usage.CacheReadInputTokens, response.StopReason);

        if (response.StopReason == "refusal")
            throw new MealAnalysisException("AI відмовився аналізувати це зображення.", canRetry: false);
        if (response.StopReason == "max_tokens")
            throw new MealAnalysisException("Відповідь AI обірвалась. Спробуйте ще раз.", canRetry: true);

        var json = string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        return MealAnalysisPrompt.Parse(json, logger);
    }

    private static Effort ParseEffort(string value) => value.ToLowerInvariant() switch
    {
        "low" => Effort.Low,
        "high" => Effort.High,
        "max" => Effort.Max,
        _ => Effort.Medium,
    };
}

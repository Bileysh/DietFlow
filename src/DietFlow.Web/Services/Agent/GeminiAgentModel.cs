using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using DietFlow.Web.Services.Ai;
using Microsoft.Extensions.Options;

namespace DietFlow.Web.Services.Agent;

public class GeminiAgentModel(HttpClient http, IOptions<GeminiOptions> options, ILogger<GeminiAgentModel> logger) : IAgentModel
{
    private readonly GeminiOptions _opt = options.Value;
    private static readonly TimeSpan PrimaryHeadersTimeout = TimeSpan.FromSeconds(30);

    private int _firstModel;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_opt.ApiKey);

    public async IAsyncEnumerable<ModelChunk> StreamAsync(
        string systemPrompt, IReadOnlyList<AgentTurn> turns, IReadOnlyList<ToolDefinition> tools,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!IsConfigured) throw new AgentException("AI-помічник не налаштований: відсутній ключ Gemini API.");

        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = systemPrompt }) },
            ["contents"] = new JsonArray(turns.Select(t => (JsonNode)new JsonObject
            {
                ["role"] = t.Role,
                ["parts"] = new JsonArray(t.Parts.Select(p => p.DeepClone()).ToArray()),
            }).ToArray()),
            ["tools"] = new JsonArray(new JsonObject
            {
                ["functionDeclarations"] = new JsonArray(tools.Select(t => (JsonNode)new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = t.Parameters.DeepClone(),
                }).ToArray()),
            }),
            ["generationConfig"] = new JsonObject { ["temperature"] = 0.4 },
        };

        using var response = await SendWithFallbackAsync(body.ToJsonString(), ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var chunk = JsonNode.Parse(line[5..].Trim());

            var candidate = chunk?["candidates"]?[0];
            var finish = candidate?["finishReason"]?.GetValue<string>();
            if (finish is "SAFETY" or "PROHIBITED_CONTENT" or "BLOCKLIST" or "SPII")
                throw new AgentException("AI відмовився відповідати на цей запит.");

            foreach (var part in candidate?["content"]?["parts"]?.AsArray() ?? [])
                if (part is JsonObject obj) yield return new ModelChunk((JsonObject)obj.DeepClone());
        }
    }

    private async Task<HttpResponseMessage> SendWithFallbackAsync(string json, CancellationToken ct)
    {
        var models = new[] { _opt.Model, _opt.FallbackModel }.Where(m => !string.IsNullOrWhiteSpace(m)).Distinct().ToArray();
        for (var i = Math.Min(_firstModel, models.Length - 1); ; i++)
        {
            var model = models[i]!;
            var isLast = i == models.Length - 1;
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:streamGenerateContent?alt=sse")
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("x-goog-api-key", _opt.ApiKey);

            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (!isLast) attemptCts.CancelAfter(PrimaryHeadersTimeout);

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, attemptCts.Token);
            }
            catch (HttpRequestException ex)
            {
                throw new AgentException("Немає зв'язку з сервісом AI. Перевірте інтернет.", ex);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("Gemini agent {Model} did not respond in time", model);
                if (isLast) throw new AgentException("AI відповідає занадто довго. Спробуйте ще раз трохи пізніше.");
                _firstModel = i + 1;
                continue;
            }

            if (response.IsSuccessStatusCode) return response;

            var raw = await response.Content.ReadAsStringAsync(ct);
            response.Dispose();
            logger.LogWarning("Gemini agent {Model} error {Status}: {Body}", model, (int)response.StatusCode, raw);

            var retryable = response.StatusCode is HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
            if (retryable && !isLast)
            {
                _firstModel = i + 1;
                continue;
            }

            throw new AgentException(response.StatusCode switch
            {
                HttpStatusCode.TooManyRequests => "Вичерпано безкоштовний ліміт запитів до AI. Спробуйте за хвилину.",
                >= HttpStatusCode.InternalServerError => "Сервіс AI тимчасово перевантажений. Спробуйте трохи пізніше.",
                _ => "AI не зміг обробити запит.",
            });
        }
    }
}

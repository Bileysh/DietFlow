using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using DietFlow.Web.Data;
using DietFlow.Web.Models.Entities;
using DietFlow.Web.Services;
using DietFlow.Web.Services.Agent;
using DietFlow.Web.Services.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DietFlow.Web.Controllers.Api;

[Route("api/v1/agent")]
[Tags("AI agent")]
[Authorize]
public class AgentApiController(
    AgentService agent, AnalysisQuotaService quota, AppDbContext db, CurrentUser currentUser, CityContext cityContext) : ApiControllerBase
{
    private const int MaxImageBytes = (int)ImageValidator.MaxBytes;
    
    [HttpPost("chat")]
    [RequestSizeLimit(MaxImageBytes * 2)]
    [Produces("text/event-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IResult> Chat(AgentChatRequest request)
    {
        byte[]? image = null;
        if (!string.IsNullOrEmpty(request.ImageBase64))
        {
            var buffer = new byte[MaxImageBytes];
            if (!Convert.TryFromBase64String(request.ImageBase64, buffer, out var length) || ImageValidator.DetectMediaType(buffer.AsSpan(0, length)) is null)
                return Validation("imageBase64", "Фото має бути JPEG, PNG або WebP до 5 МБ.");
            image = buffer[..length];
        }
        if (string.IsNullOrWhiteSpace(request.Message) && image is null)
            return Validation("message", "Напишіть повідомлення або прикріпіть фото.");
        if (request.History is { Count: > AgentService.MaxHistory * 2 })
            return Validation("history", $"Історія — не більше {AgentService.MaxHistory * 2} повідомлень.");

        if (!agent.IsAvailable)
            return TypedResults.Problem(detail: "AI provider is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);

        var userId = await currentUser.GetIdAsync();
        var limits = await quota.GetAsync(userId, AiUsageKind.AgentMessage, HttpContext.RequestAborted);
        if (limits.Exhausted)
            return TypedResults.Problem(title: "Daily agent message limit reached",
                detail: $"На плані Free доступно {limits.Limit} повідомлень помічнику на день. Premium знімає обмеження.",
                statusCode: StatusCodes.Status429TooManyRequests);

        db.AiAnalysisLogs.Add(new AiAnalysisLog { UserId = userId, Kind = AiUsageKind.AgentMessage });
        await db.SaveChangesAsync();

        var city = await cityContext.GetCurrentAsync();
        var ctx = new AgentContext(userId, city?.Id ?? 0, city?.Name, request.Lat, request.Lng, image);
        var events = agent.RunAsync(request.Message?.Trim() ?? "", request.History ?? [], ctx, HttpContext.RequestAborted);

        Response.Headers["X-Accel-Buffering"] = "no";
        return TypedResults.ServerSentEvents(AsSse(events));
    }

    private static async IAsyncEnumerable<SseItem<AgentEvent>> AsSse(IAsyncEnumerable<AgentEvent> events,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var e in events.WithCancellation(ct))
            yield return new SseItem<AgentEvent>(e, e.Type);
    }

    private static IResult Validation(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}

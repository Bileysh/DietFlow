using Microsoft.ApplicationInsights;

namespace DietFlow.Web.Services;

public class AppEvents(IServiceProvider services, ILogger<AppEvents> logger)
{
    private readonly TelemetryClient? _client = services.GetService<TelemetryClient>();

    public const string MealPhotoAnalyzed = "MealPhotoAnalyzed";
    public const string AgentMessage = "AgentMessage";
    public const string UserSignedIn = "UserSignedIn";
    public const string PlanChanged = "PlanChanged";
    public const string ApiTokenCreated = "ApiTokenCreated";
    public const string TelegramLinked = "TelegramLinked";
    public const string McpToolCalled = "McpToolCalled";

    public void Track(string name, params (string Key, object? Value)[] properties)
    {
        var props = properties.Where(p => p.Value is not null)
            .ToDictionary(p => p.Key, p => Convert.ToString(p.Value, System.Globalization.CultureInfo.InvariantCulture)!);
        _client?.TrackEvent(name, props);
        logger.LogInformation("Event {Event}: {Properties}", name, string.Join(", ", props.Select(p => $"{p.Key}={p.Value}")));
    }
}

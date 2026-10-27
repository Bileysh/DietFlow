using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DietFlow.Web.Services.Ai;

namespace DietFlow.Web.Services.Agent;

public record AgentHistoryItem(string Role, string Text);

public class AgentChatRequest
{
    [MaxLength(2000)]
    public string? Message { get; set; }

    public List<AgentHistoryItem>? History { get; set; }

    public string? ImageBase64 { get; set; }

    [Range(-90, 90)] public double? Lat { get; set; }
    [Range(-180, 180)] public double? Lng { get; set; }
}

public record AgentEvent(
    string Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Tool = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Ok = null)
{
    public static AgentEvent Delta(string text) => new("delta", text);
    public static AgentEvent Step(string tool, string label) => new("step", label, tool);
    public static AgentEvent Result(string tool, string summary, bool ok) => new("result", summary, tool, ok);
    public static AgentEvent Done() => new("done");
    public static AgentEvent Error(string message) => new("error", message);
}

public class AgentContext(string userId, int cityId, string? cityName, double? lat, double? lng, byte[]? image)
{
    public string UserId { get; } = userId;
    public int CityId { get; } = cityId;
    public string? CityName { get; } = cityName;
    public double? Lat { get; } = lat;
    public double? Lng { get; } = lng;
    public byte[]? Image { get; } = image;

    public MealAnalysis? LastPhotoAnalysis { get; set; }
}

public record ToolDefinition(string Name, string Description, JsonObject Parameters);

public record AgentTurn(string Role, List<JsonNode> Parts);

public record ModelChunk(JsonObject Part)
{
    public string? Text => Part["thought"]?.GetValue<bool>() == true ? null : Part["text"]?.GetValue<string>();
    public string? CallName => Part["functionCall"]?["name"]?.GetValue<string>();
    public JsonObject CallArgs => Part["functionCall"]?["args"] as JsonObject ?? [];
}

public interface IAgentModel
{
    bool IsConfigured { get; }

    IAsyncEnumerable<ModelChunk> StreamAsync(
        string systemPrompt, IReadOnlyList<AgentTurn> turns, IReadOnlyList<ToolDefinition> tools, CancellationToken ct);
}

public class AgentException(string message, Exception? inner = null) : Exception(message, inner);

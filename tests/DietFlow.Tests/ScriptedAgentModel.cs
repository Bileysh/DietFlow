using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using DietFlow.Web.Services.Agent;

namespace DietFlow.Tests;

public class ScriptedAgentModel : IAgentModel
{
    public Func<IReadOnlyList<AgentTurn>, IEnumerable<JsonObject>> Script { get; set; } = _ => [Text("Привіт!")];

    public bool IsConfigured => true;

    public async IAsyncEnumerable<ModelChunk> StreamAsync(
        string systemPrompt, IReadOnlyList<AgentTurn> turns, IReadOnlyList<ToolDefinition> tools,
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var part in Script(turns))
        {
            await Task.Yield();
            yield return new ModelChunk(part);
        }
    }

    public static JsonObject Text(string text) => new() { ["text"] = text };

    public static JsonObject Call(string name, JsonObject? args = null) =>
        new() { ["functionCall"] = new JsonObject { ["name"] = name, ["args"] = args ?? [] } };

    public static JsonNode? LastToolResponse(IReadOnlyList<AgentTurn> turns) =>
        turns[^1].Role == "user" ? turns[^1].Parts.LastOrDefault()?["functionResponse"]?["response"] : null;
}

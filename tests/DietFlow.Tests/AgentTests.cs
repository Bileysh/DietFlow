using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DietFlow.Web.Data;
using DietFlow.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static DietFlow.Tests.ScriptedAgentModel;

namespace DietFlow.Tests;

[Collection(WebCollection.Name)]
public class AgentTests(DietFlowFactory factory)
{
    private record SseEvent(string Type, string? Text, string? Tool, bool? Ok);

    [Fact]
    public async Task Anonymous_Returns401()
    {
        var response = await TestUsers.Client(factory).PostAsJsonAsync("/api/v1/agent/chat", new { message = "Привіт" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task EmptyMessageWithoutPhoto_Returns400()
    {
        var user = await TestUsers.CreateAsync(factory);

        var response = await TestUsers.Client(factory, user).PostAsJsonAsync("/api/v1/agent/chat", new { message = " " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PlainAnswer_StreamsTextAsServerSentEvents()
    {
        var user = await TestUsers.CreateAsync(factory);
        factory.AgentModel.Script = _ => [Text("Добрий "), Text("день!")];

        var (response, events) = await ChatAsync(user, "Привіт");

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(["delta", "delta", "done"], events.Select(e => e.Type));
        Assert.Equal("Добрий день!", string.Concat(events.Where(e => e.Type == "delta").Select(e => e.Text)));
    }

    [Fact]
    public async Task ToolCall_IsExecuted_AndResultGoesBackToModel()
    {
        var user = await TestUsers.CreateAsync(factory);
        await AddMealAsync(user.Id, "Омлет", 350);

        factory.AgentModel.Script = turns => LastToolResponse(turns) is { } r
            ? [Text($"Сьогодні {r["result"]!["totals"]!["kcal"]} ккал")]
            : [Call("get_diary")];

        var (_, events) = await ChatAsync(user, "Скільки я з'їв?");

        Assert.Equal(["step", "result", "delta", "done"], events.Select(e => e.Type));
        Assert.Equal("get_diary", events[0].Tool);
        Assert.True(events[1].Ok);
        Assert.Equal("Сьогодні 350 ккал", events[2].Text);
    }

    [Fact]
    public async Task Diary_ShowsOnlyOwnMeals_NotOtherUsers()
    {
        var user = await TestUsers.CreateAsync(factory);
        var other = await TestUsers.CreateAsync(factory);
        await AddMealAsync(other.Id, "Чужий торт", 900);

        JsonNode? seen = null;
        factory.AgentModel.Script = turns =>
        {
            if (LastToolResponse(turns) is not { } r) return [Call("get_diary")];
            seen = r;
            return [Text("ok")];
        };

        await ChatAsync(user, "Що в моєму щоденнику?");

        Assert.Equal(0, seen!["result"]!["meals"]!.AsArray().Count);
        Assert.DoesNotContain("Чужий торт", seen.ToJsonString());
    }

    [Fact]
    public async Task AddMeal_WritesToCurrentUsersDiary()
    {
        var user = await TestUsers.CreateAsync(factory);
        factory.AgentModel.Script = turns => LastToolResponse(turns) is null
            ? [Call("add_meal", new JsonObject { ["type"] = "Lunch", ["dishName"] = "Борщ", ["kcal"] = 420 })]
            : [Text("Додав!")];

        var (_, events) = await ChatAsync(user, "Додай борщ на обід, 420 ккал");

        Assert.Contains(events, e => e.Type == "result" && e.Tool == "add_meal" && e.Ok == true);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var meal = await db.Meals.SingleAsync(m => m.UserId == user.Id);
        Assert.Equal(("Борщ", 420, MealType.Lunch), (meal.DishName, meal.Kcal, meal.Type));
    }

    [Fact]
    public async Task ToolError_IsReportedAndModelContinues()
    {
        var user = await TestUsers.CreateAsync(factory);
        factory.AgentModel.Script = turns => LastToolResponse(turns) is { } r
            ? [Text("Помилка: " + r["error"])]
            : [Call("get_recipe", new JsonObject { ["recipeId"] = 2147483647 })];

        var (_, events) = await ChatAsync(user, "Покажи рецепт");

        var result = Assert.Single(events, e => e.Type == "result");
        Assert.False(result.Ok);
        Assert.Equal("done", events[^1].Type);
    }

    [Fact]
    public async Task EndlessToolCalls_StopAfterMaxSteps_WithError()
    {
        var user = await TestUsers.CreateAsync(factory);
        factory.AgentModel.Script = _ => [Call("get_diary")];

        var (_, events) = await ChatAsync(user, "Цикл");

        Assert.Equal(6, events.Count(e => e.Type == "step"));
        Assert.Equal("error", events[^1].Type);
    }

    [Fact]
    public async Task FreePlan_21stMessageInADay_Returns429()
    {
        var user = await TestUsers.CreateAsync(factory, SubscriptionPlan.Free);
        await AddUsageAsync(user.Id, 20);

        var response = await TestUsers.Client(factory, user).PostAsJsonAsync("/api/v1/agent/chat", new { message = "Ще одне" });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task PremiumPlan_HasNoAgentMessageLimit()
    {
        var user = await TestUsers.CreateAsync(factory, SubscriptionPlan.Premium);
        await AddUsageAsync(user.Id, 20);
        factory.AgentModel.Script = _ => [Text("ok")];

        var (response, _) = await ChatAsync(user, "Ще одне");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<(HttpResponseMessage Response, List<SseEvent> Events)> ChatAsync(TestUser user, string message)
    {
        var response = await TestUsers.Client(factory, user).PostAsJsonAsync("/api/v1/agent/chat", new { message });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var events = body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(block => block.Split('\n').FirstOrDefault(l => l.StartsWith("data:")))
            .Where(line => line is not null)
            .Select(line => JsonSerializer.Deserialize<SseEvent>(line![5..].Trim(), JsonSerializerOptions.Web)!)
            .ToList();
        return (response, events);
    }

    private async Task AddMealAsync(string userId, string dish, int kcal)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Meals.Add(new Meal { UserId = userId, Type = MealType.Breakfast, DishName = dish, Kcal = kcal, EatenAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    private async Task AddUsageAsync(string userId, int count)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AiAnalysisLogs.AddRange(Enumerable.Range(0, count).Select(_ => new AiAnalysisLog { UserId = userId, Kind = AiUsageKind.AgentMessage }));
        await db.SaveChangesAsync();
    }
}

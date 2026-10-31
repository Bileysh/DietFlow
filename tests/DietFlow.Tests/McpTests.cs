using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DietFlow.Web.Data;
using DietFlow.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DietFlow.Tests;

[Collection(WebCollection.Name)]
public class McpTests(DietFlowFactory factory)
{
    [Fact]
    public async Task WithoutToken_Returns401()
    {
        var response = await TestUsers.Client(factory).PostAsync("/mcp", new StringContent("{}", null, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListsReadAndWriteTools()
    {
        await using var client = await ConnectAsync(await TestUsers.CreateAsync(factory));

        var tools = await client.ListToolsAsync();

        var readOnly = tools.Where(t => t.ProtocolTool.Annotations?.ReadOnlyHint == true).Select(t => t.Name).ToList();
        var write = tools.Where(t => t.ProtocolTool.Annotations?.ReadOnlyHint == false).Select(t => t.Name).ToList();
        Assert.True(readOnly.Count >= 3, string.Join(", ", readOnly));
        Assert.Contains("add_meal", write);
    }

    [Fact]
    public async Task GetDiary_ReturnsOnlyTokenOwnersMeals()
    {
        var owner = await TestUsers.CreateAsync(factory);
        var other = await TestUsers.CreateAsync(factory);
        await AddMealAsync(owner.Id, "Сирники", 450);
        await AddMealAsync(other.Id, "Чужа піца", 1200);
        await using var client = await ConnectAsync(owner);

        var result = await client.CallToolAsync("get_diary", new Dictionary<string, object?>());

        var json = JsonDocument.Parse(TextOf(result)).RootElement;
        Assert.NotEqual(true, result.IsError);
        Assert.Equal(450, json.GetProperty("totals").GetProperty("kcal").GetInt32());
        Assert.DoesNotContain("Чужа піца", TextOf(result));
    }

    [Fact]
    public async Task AddMeal_WritesToTokenOwnersDiary()
    {
        var user = await TestUsers.CreateAsync(factory);
        await using var client = await ConnectAsync(user);

        var result = await client.CallToolAsync("add_meal", new Dictionary<string, object?>
        {
            ["type"] = "Dinner", ["dishName"] = "Гречка з куркою", ["kcal"] = 520,
        });

        Assert.True(result.IsError != true, TextOf(result));
        using var scope = factory.Services.CreateScope();
        var meal = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Meals.SingleAsync(m => m.UserId == user.Id);
        Assert.Equal(("Гречка з куркою", 520), (meal.DishName, meal.Kcal));
    }

    [Fact]
    public async Task ToolError_IsReturnedAsErrorResult()
    {
        await using var client = await ConnectAsync(await TestUsers.CreateAsync(factory));

        var result = await client.CallToolAsync("get_recipe", new Dictionary<string, object?> { ["recipeId"] = 2147483647 });

        Assert.True(result.IsError);
        Assert.Contains("не знайдено", TextOf(result));
    }

    private async Task<McpClient> ConnectAsync(TestUser user)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http, ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    private static string TextOf(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));

    private async Task AddMealAsync(string userId, string dish, int kcal)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Meals.Add(new Meal { UserId = userId, Type = MealType.Lunch, DishName = dish, Kcal = kcal, EatenAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }
}

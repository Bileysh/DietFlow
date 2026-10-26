using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DietFlow.Web.Models.Entities;
using DietFlow.Web.Services.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace DietFlow.Tests;

[Collection(WebCollection.Name)]
public class AuthTests(DietFlowFactory factory)
{
    private static readonly object NewProduct = new
    {
        name = "Тестовий продукт", kcalPer100g = 100, proteinPer100g = 10, fatPer100g = 5, carbsPer100g = 5,
    };


    [Theory]
    [InlineData("/Meals")]
    [InlineData("/Profile")]
    [InlineData("/MealPlan")]
    public async Task Anonymous_PrivatePage_RedirectsToLogin(string url)
    {
        var response = await TestUsers.Client(factory).GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Anonymous_PrivateApi_Returns401_NotRedirect()
    {
        var response = await TestUsers.Client(factory).GetAsync("/api/v1/meals");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/products?limit=1")]
    [InlineData("/api/v1/recipes?limit=1")]
    [InlineData("/Account/Login")]
    [InlineData("/Premium")]
    public async Task Anonymous_PublicPages_AreOpen(string url)
    {
        var response = await TestUsers.Client(factory).GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }


    [Fact]
    public async Task ApiToken_Valid_GivesAccess()
    {
        var user = await TestUsers.CreateAsync(factory);

        var response = await TestUsers.Client(factory, user).GetAsync("/api/v1/meals");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("df_this-token-does-not-exist")]
    [InlineData("not-even-our-format")]
    public async Task ApiToken_Invalid_Returns401(string token)
    {
        var client = TestUsers.Client(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/meals");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ApiToken_Revoked_Returns401()
    {
        var user = await TestUsers.CreateAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var tokens = scope.ServiceProvider.GetRequiredService<ApiTokenService>();
            var token = (await tokens.ListAsync(user.Id)).Single();
            Assert.True(await tokens.RevokeAsync(user.Id, token.Id));
        }

        var response = await TestUsers.Client(factory, user).GetAsync("/api/v1/meals");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ApiToken_StoredOnlyAsHash()
    {
        var user = await TestUsers.CreateAsync(factory);
        using var scope = factory.Services.CreateScope();
        var stored = (await scope.ServiceProvider.GetRequiredService<ApiTokenService>().ListAsync(user.Id)).Single();

        Assert.NotEqual(user.Token, stored.TokenHash);
        Assert.DoesNotContain(user.Token, stored.TokenHash);
        Assert.Equal(64, stored.TokenHash.Length);
    }


    [Fact]
    public async Task Meal_OfAnotherUser_IsNotVisible_404()
    {
        var owner = await TestUsers.CreateAsync(factory);
        var stranger = await TestUsers.CreateAsync(factory);
        var mealId = await CreateMealAsync(owner);
        var strangerClient = TestUsers.Client(factory, stranger);

        Assert.Equal(HttpStatusCode.NotFound, (await strangerClient.GetAsync($"/api/v1/meals/{mealId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await strangerClient.PutAsJsonAsync($"/api/v1/meals/{mealId}", new { type = "Dinner" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await strangerClient.DeleteAsync($"/api/v1/meals/{mealId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await TestUsers.Client(factory, owner).GetAsync($"/api/v1/meals/{mealId}")).StatusCode);
    }

    [Fact]
    public async Task MealsList_ContainsOnlyOwnMeals()
    {
        var owner = await TestUsers.CreateAsync(factory);
        var other = await TestUsers.CreateAsync(factory);
        await CreateMealAsync(owner);
        await CreateMealAsync(other);

        var page = await TestUsers.Client(factory, owner).GetFromJsonAsync<JsonElement>("/api/v1/meals");

        Assert.Equal(1, page.GetProperty("items").GetArrayLength());
    }


    [Fact]
    public async Task Catalog_NonAdmin_CannotCreateProduct_403()
    {
        var user = await TestUsers.CreateAsync(factory);

        var response = await TestUsers.Client(factory, user).PostAsJsonAsync("/api/v1/products", NewProduct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Prices_NonAdmin_CannotSetOrDeletePrice_403()
    {
        var client = TestUsers.Client(factory, await TestUsers.CreateAsync(factory));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/v1/stores/1/prices/1", new { pricePerKg = 50 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync("/api/v1/stores/1/prices/1")).StatusCode);
    }

    [Fact]
    public async Task Catalog_Admin_CanCreateAndDeleteProduct()
    {
        var admin = await TestUsers.CreateAsync(factory, admin: true);
        var client = TestUsers.Client(factory, admin);

        var created = await client.PostAsJsonAsync("/api/v1/products", NewProduct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/products/{id}")).StatusCode);
    }

    [Theory]
    [InlineData("/Products/Create")]
    [InlineData("/Stores/Create")]
    [InlineData("/Recipes/Create")]
    public async Task Catalog_NonAdmin_MvcEditPages_Forbidden(string url)
    {
        var user = await TestUsers.CreateAsync(factory);

        var response = await TestUsers.Client(factory, user).GetAsync(url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }


    [Fact]
    public async Task FreePlan_SixthAnalysisInADay_Returns429()
    {
        var user = await TestUsers.CreateAsync(factory, SubscriptionPlan.Free);
        var client = TestUsers.Client(factory, user);
        var mealId = await CreateMealWithPhotoAsync(client);

        for (var i = 1; i <= 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/meals/{mealId}/analyze", null)).StatusCode);

        var sixth = await client.PostAsync($"/api/v1/meals/{mealId}/analyze", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
    }

    [Fact]
    public async Task PremiumPlan_HasNoDailyAnalysisLimit()
    {
        var user = await TestUsers.CreateAsync(factory, SubscriptionPlan.Premium);
        var client = TestUsers.Client(factory, user);
        var mealId = await CreateMealWithPhotoAsync(client);

        for (var i = 1; i <= 7; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/meals/{mealId}/analyze", null)).StatusCode);
    }

    [Fact]
    public async Task WeeklyPlan_Free_RedirectsToPremiumPage()
    {
        var user = await TestUsers.CreateAsync(factory, SubscriptionPlan.Free);

        var response = await TestUsers.Client(factory, user).GetAsync("/MealPlan");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Premium", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task WeeklyPlan_Premium_IsAvailable()
    {
        var user = await TestUsers.CreateAsync(factory, SubscriptionPlan.Premium);

        var response = await TestUsers.Client(factory, user).GetAsync("/MealPlan");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }


    private async Task<int> CreateMealAsync(TestUser user)
    {
        var response = await TestUsers.Client(factory, user).PostAsJsonAsync("/api/v1/meals", new { type = "Lunch", dishName = "Тест" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    private static async Task<int> CreateMealWithPhotoAsync(HttpClient client)
    {
        var created = await client.PostAsJsonAsync("/api/v1/meals", new { type = "Lunch" });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var jpeg = new byte[64];
        jpeg[0] = 0xFF; jpeg[1] = 0xD8; jpeg[2] = 0xFF;
        var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(jpeg) { Headers = { ContentType = new MediaTypeHeaderValue("image/jpeg") } }, "photo", "meal.jpg" },
        };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/meals/{id}/photo", form)).StatusCode);
        return id;
    }
}

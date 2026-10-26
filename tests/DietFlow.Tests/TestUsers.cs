using System.Net.Http.Headers;
using DietFlow.Web.Models.Entities;
using DietFlow.Web.Services.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DietFlow.Tests;

public record TestUser(string Id, string Email, string Token);

public static class TestUsers
{
    public static async Task<TestUser> CreateAsync(DietFlowFactory factory, SubscriptionPlan plan = SubscriptionPlan.Free, bool admin = false)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var accounts = scope.ServiceProvider.GetRequiredService<UserAccountService>();
        var tokens = scope.ServiceProvider.GetRequiredService<ApiTokenService>();

        var email = admin ? DietFlowFactory.AdminEmail : $"user-{Guid.NewGuid():N}@test.local";
        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = email };
            var result = await users.CreateAsync(user);
            Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
        }
        user.Plan = plan;
        await users.UpdateAsync(user);
        await accounts.SyncAdminRoleAsync(user);

        var (_, token) = await tokens.CreateAsync(user.Id, "tests");
        return new TestUser(user.Id, email, token);
    }

    public static HttpClient Client(DietFlowFactory factory, TestUser? user = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (user is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        return client;
    }
}

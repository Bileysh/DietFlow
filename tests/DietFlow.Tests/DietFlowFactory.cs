using DietFlow.Web.Data;
using DietFlow.Web.Services;
using DietFlow.Web.Services.Ai;
using DietFlow.Web.Services.Agent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace DietFlow.Tests;

public class DietFlowFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@test.local";

    private static readonly string ConnectionString = BuildTestConnectionString();

    public ScriptedAgentModel AgentModel { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Admin:Emails:0", AdminEmail);
        builder.UseSetting("Plans:FreeDailyAnalyses", "5");
        builder.UseSetting("Plans:FreeDailyAgentMessages", "20");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMealAnalyzer>();
            services.AddScoped<IMealAnalyzer, FakeMealAnalyzer>();
            services.RemoveAll<IFileStorage>();
            services.AddSingleton<IFileStorage, InMemoryFileStorage>();
            services.RemoveAll<IAgentModel>();
            services.AddSingleton<IAgentModel>(AgentModel);
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;
    
    private static string BuildTestConnectionString()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets(typeof(Program).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();
        var baseConnection = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Не задано ConnectionStrings:Default (user-secrets застосунку або змінна оточення).");
        return new NpgsqlConnectionStringBuilder(baseConnection) { Database = "dietflow_test" }.ConnectionString;
    }
}

[CollectionDefinition(Name)]
public class WebCollection : ICollectionFixture<DietFlowFactory>
{
    public const string Name = "web";
}

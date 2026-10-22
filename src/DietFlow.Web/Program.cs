using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.Extensions.WebEncoders;
using DietFlow.Web.Data;
using DietFlow.Web.Services;
using DietFlow.Web.Services.Ai;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CityContext>();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<RecipeCalculator>();
builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();

builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection("Anthropic"));
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection("Gemini"));
builder.Services.AddSingleton<ClaudeMealAnalyzer>();
builder.Services.AddHttpClient<GeminiMealAnalyzer>(c => c.Timeout = TimeSpan.FromSeconds(90));
builder.Services.AddScoped<IMealAnalyzer>(sp =>
    string.Equals(builder.Configuration["Ai:Provider"], "Claude", StringComparison.OrdinalIgnoreCase)
        ? sp.GetRequiredService<ClaudeMealAnalyzer>()
        : sp.GetRequiredService<GeminiMealAnalyzer>());
builder.Services.AddScoped<MealAnalysisService>();

builder.Services.AddScoped<CacheInvalidator>();
builder.Services.Configure<SiteOptions>(builder.Configuration.GetSection("Site"));

builder.Services.AddControllersWithViews(o =>
    {
        var m = o.ModelBindingMessageProvider;
        m.SetValueMustNotBeNullAccessor(_ => "Поле обов'язкове.");
        m.SetMissingKeyOrValueAccessor(() => "Поле обов'язкове.");
        m.SetAttemptedValueIsInvalidAccessor((value, _) => $"Значення «{value}» некоректне.");
        m.SetValueIsInvalidAccessor(value => $"Значення «{value}» некоректне.");
        m.SetNonPropertyAttemptedValueIsInvalidAccessor(value => $"Значення «{value}» некоректне.");
    })
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    });

builder.Services.AddProblemDetails();

builder.Services.AddOutputCache(o =>
    o.AddPolicy(CacheTags.Policy, b => b.Expire(TimeSpan.FromSeconds(60)).SetVaryByQuery("*")));

builder.Services.AddOpenApi(o =>
{
    o.ShouldInclude = d => d.RelativePath?.StartsWith("api/v1", StringComparison.Ordinal) == true;
    o.AddDocumentTransformer((doc, _, _) =>
    {
        doc.Info.Title = "DietFlow API";
        doc.Info.Description = "REST API DietFlow: продукти, магазини й ціни, рецепти з розрахунком калорій і вартості, щоденник з AI-аналізом фото. " +
                               "Списки підтримують пагінацію skip/limit з nextLink; GET-відповіді кешуються на 60 с.";
        return Task.CompletedTask;
    });
});

builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
    db.Database.Migrate();
    db.Database.SetCommandTimeout(null);
    await DbSeeder.SeedAsync(db, app.Environment.WebRootPath);
}

if (args.Contains("import-off"))
{
    var limitIndex = Array.IndexOf(args, "--limit");
    int? limit = limitIndex >= 0 && limitIndex + 1 < args.Length && int.TryParse(args[limitIndex + 1], out var l) ? l : null;
    var importer = new DietFlow.Web.Data.Import.OpenFoodFactsImporter(
        builder.Configuration.GetConnectionString("Default")!,
        app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("OpenFoodFacts"));
    await importer.RunAsync(limit);
    return;
}

var appCulture = (CultureInfo)CultureInfo.GetCultureInfo("uk-UA").Clone();
appCulture.NumberFormat.NumberDecimalSeparator = ".";
appCulture.NumberFormat.CurrencyDecimalSeparator = ".";
var localization = new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(appCulture),
    SupportedCultures = [appCulture],
    SupportedUICultures = [appCulture],
};

localization.RequestCultureProviders.Clear();
app.UseRequestLocalization(localization);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/api"), api => api.UseExceptionHandler());

app.UseRouting();
app.UseOutputCache();

app.UseAuthorization();

app.MapStaticAssets();

app.MapOpenApi();                     
app.MapScalarApiReference(o => o.WithTitle("DietFlow API")); 

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();

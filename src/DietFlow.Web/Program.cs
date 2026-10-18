using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.Extensions.WebEncoders;
using DietFlow.Web.Data;
using DietFlow.Web.Services;
using DietFlow.Web.Services.Ai;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;

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

builder.Services.AddControllersWithViews();

builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    await DbSeeder.SeedAsync(db, app.Environment.WebRootPath);
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
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();

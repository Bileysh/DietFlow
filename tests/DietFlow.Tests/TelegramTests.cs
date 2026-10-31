using DietFlow.Web.Data;
using DietFlow.Web.Models.Entities;
using DietFlow.Web.Services.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static DietFlow.Tests.ScriptedAgentModel;

namespace DietFlow.Tests;

[Collection(WebCollection.Name)]
public class TelegramTests(DietFlowFactory factory)
{
    private static long _nextChat = 1_000_000;
    private static long NewChatId() => Interlocked.Increment(ref _nextChat);

    [Fact]
    public async Task UnlinkedChat_GetsNoData()
    {
        var chat = NewChatId();

        await HandleAsync(new TgUpdate(chat, "/today"));

        Assert.Contains("не підключено", Last(chat));
    }

    [Fact]
    public async Task Start_WithValidCode_LinksChatAndClearsCode()
    {
        var user = await TestUsers.CreateAsync(factory);
        var chat = NewChatId();
        var code = await CreateCodeAsync(user.Id);

        await HandleAsync(new TgUpdate(chat, $"/start {code}"));

        Assert.Contains("Підключено", Last(chat));
        var stored = await GetUserAsync(user.Id);
        Assert.Equal(chat, stored.TelegramChatId);
        Assert.Null(stored.TelegramLinkCode);
    }

    [Fact]
    public async Task Start_WithExpiredCode_DoesNotLink()
    {
        var user = await TestUsers.CreateAsync(factory);
        var chat = NewChatId();
        var code = await CreateCodeAsync(user.Id, expiresAt: DateTime.UtcNow.AddMinutes(-1));

        await HandleAsync(new TgUpdate(chat, $"/start {code}"));

        Assert.Contains("недійсний", Last(chat));
        Assert.Null((await GetUserAsync(user.Id)).TelegramChatId);
    }

    [Fact]
    public async Task Start_CodeCannotBeReused()
    {
        var user = await TestUsers.CreateAsync(factory);
        var code = await CreateCodeAsync(user.Id);
        await HandleAsync(new TgUpdate(NewChatId(), $"/start {code}"));

        var attacker = NewChatId();
        await HandleAsync(new TgUpdate(attacker, $"/start {code}"));

        Assert.Contains("недійсний", Last(attacker));
    }

    [Fact]
    public async Task Today_ShowsOnlyOwnMeals()
    {
        var (user, chat) = await LinkedUserAsync();
        var other = await TestUsers.CreateAsync(factory);
        await AddMealAsync(user.Id, "Вівсянка", 300);
        await AddMealAsync(other.Id, "Чужий бургер", 900);

        await HandleAsync(new TgUpdate(chat, "/today"));

        var text = Last(chat);
        Assert.Contains("Вівсянка", text);
        Assert.Contains("300 з 2000", text);
        Assert.DoesNotContain("Чужий бургер", text);
    }

    [Fact]
    public async Task Photo_IsAnalyzed_AndButtonAddsMealWithPhoto()
    {
        var (user, chat) = await LinkedUserAsync();

        await HandleAsync(new TgUpdate(chat, PhotoFileId: "file-1"));
        var analysis = factory.Telegram.For(chat)[^1];
        Assert.Contains("Тестова страва", analysis.Text);
        Assert.Contains(analysis.Buttons, b => b.Data == "add:Lunch");

        await HandleAsync(new TgUpdate(chat, CallbackId: "cb-1", CallbackData: "add:Lunch"));

        using var scope = factory.Services.CreateScope();
        var meal = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Meals.SingleAsync(m => m.UserId == user.Id);
        Assert.Equal((MealType.Lunch, "Тестова страва", 400), (meal.Type, meal.DishName, meal.Kcal));
        Assert.NotNull(meal.PhotoPath);
    }

    [Fact]
    public async Task Text_IsAnsweredByAgent_WithAbsoluteLinks()
    {
        var (_, chat) = await LinkedUserAsync();
        factory.AgentModel.Script = _ => [Text("Спробуйте **[Гречка з куркою](/recipes/hrechka)**")];

        await HandleAsync(new TgUpdate(chat, "Що на вечерю?"));

        Assert.Equal("Спробуйте Гречка з куркою (http://localhost:5213/recipes/hrechka)", Last(chat));
    }

    [Fact]
    public async Task Unlink_RemovesAccess()
    {
        var (user, chat) = await LinkedUserAsync();

        await HandleAsync(new TgUpdate(chat, "/unlink"));
        await HandleAsync(new TgUpdate(chat, "/today"));

        Assert.Null((await GetUserAsync(user.Id)).TelegramChatId);
        Assert.Contains("не підключено", Last(chat));
    }
    
    private async Task HandleAsync(TgUpdate update)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TelegramBotHandler>().HandleAsync(update);
    }

    private string Last(long chat) => factory.Telegram.For(chat)[^1].Text;

    private async Task<(TestUser User, long Chat)> LinkedUserAsync()
    {
        var user = await TestUsers.CreateAsync(factory);
        var chat = NewChatId();
        await HandleAsync(new TgUpdate(chat, $"/start {await CreateCodeAsync(user.Id)}"));
        return (user, chat);
    }

    private async Task<string> CreateCodeAsync(string userId, DateTime? expiresAt = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        var code = await TelegramBotHandler.CreateLinkCodeAsync(db, user);
        if (expiresAt is not null)
        {
            user.TelegramLinkExpiresAt = expiresAt;
            await db.SaveChangesAsync();
        }
        return code;
    }

    private async Task<AppUser> GetUserAsync(string userId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking().SingleAsync(u => u.Id == userId);
    }

    private async Task AddMealAsync(string userId, string dish, int kcal)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Meals.Add(new Meal { UserId = userId, Type = MealType.Breakfast, DishName = dish, Kcal = kcal, EatenAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }
}

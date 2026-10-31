using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DietFlow.Web.Data;
using DietFlow.Web.Models.Entities;
using DietFlow.Web.Services.Agent;
using DietFlow.Web.Services.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DietFlow.Web.Services.Telegram;

public partial class TelegramBotHandler(
    AppDbContext db, ITelegramGateway tg, MealAnalysisService analysis, AnalysisQuotaService quota, AgentService agent,
    IFileStorage files, CityContext cityContext, IMemoryCache cache, IOptions<SiteOptions> site, AppEvents events, ILogger<TelegramBotHandler> logger)
{
    public static readonly TimeSpan LinkCodeLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan PendingPhotoLifetime = TimeSpan.FromMinutes(30);
    private const int ChatHistorySize = 10;

    private record PendingPhoto(byte[] Image, MealAnalysis Analysis);

    public static async Task<string> CreateLinkCodeAsync(AppDbContext db, AppUser user)
    {
        user.TelegramLinkCode = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        user.TelegramLinkExpiresAt = DateTime.UtcNow + LinkCodeLifetime;
        await db.SaveChangesAsync();
        return user.TelegramLinkCode;
    }

    public async Task HandleAsync(TgUpdate update, CancellationToken ct = default)
    {
        if (update.CallbackId is not null)
        {
            await HandleCallbackAsync(update, ct);
            return;
        }

        var text = update.Text?.Trim() ?? "";
        var command = text.StartsWith('/') ? text.Split(' ', 2)[0].Split('@')[0].ToLowerInvariant() : null;

        if (command == "/start")
        {
            await StartAsync(update.ChatId, text.Split(' ', 2).ElementAtOrDefault(1)?.Trim(), ct);
            return;
        }
        if (command == "/help")
        {
            await tg.SendTextAsync(update.ChatId, HelpText, ct: ct);
            return;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.TelegramChatId == update.ChatId, ct);
        if (user is null)
        {
            await tg.SendTextAsync(update.ChatId,
                $"Цей чат ще не підключено до DietFlow. Відкрийте профіль на сайті і натисніть «Підключити Telegram»: {site.Value.BaseUrl}/Profile", ct: ct);
            return;
        }

        switch (command)
        {
            case "/today":
                await TodayAsync(update.ChatId, user, ct);
                break;
            case "/unlink":
                user.TelegramChatId = null;
                await db.SaveChangesAsync(ct);
                await tg.SendTextAsync(update.ChatId, "Чат відв'язано від DietFlow. Щоб підключити знову — кнопка в профілі на сайті.", ct: ct);
                break;
            case not null:
                await tg.SendTextAsync(update.ChatId, HelpText, ct: ct);
                break;
            default:
                if (update.PhotoFileId is not null) await PhotoAsync(update.ChatId, user, update.PhotoFileId, ct);
                else if (text.Length > 0) await AskAgentAsync(update.ChatId, user, text, ct);
                break;
        }
    }

    private const string HelpText =
        "Я бот DietFlow 🥗\n" +
        "📷 Надішліть фото страви — порахую калорії та БЖВ і додам у щоденник.\n" +
        "💬 Напишіть питання — напр. «вечеря до 500 ккал і 80 грн» або «де найдешевше гречка».\n" +
        "/today — що я з'їв сьогодні\n/unlink — відв'язати чат\n/help — ця довідка";

    private async Task StartAsync(long chatId, string? code, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(code))
        {
            await tg.SendTextAsync(chatId, HelpText + $"\n\nЩоб почати, підключіть акаунт: {site.Value.BaseUrl}/Profile → «Підключити Telegram».", ct: ct);
            return;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.TelegramLinkCode == code && u.TelegramLinkExpiresAt > DateTime.UtcNow, ct);
        if (user is null)
        {
            await tg.SendTextAsync(chatId, "Код недійсний або прострочений. Створіть нове посилання в профілі на сайті.", ct: ct);
            return;
        }

        await db.Users.Where(u => u.TelegramChatId == chatId && u.Id != user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.TelegramChatId, (long?)null), ct);
        user.TelegramChatId = chatId;
        user.TelegramLinkCode = null;
        user.TelegramLinkExpiresAt = null;
        await db.SaveChangesAsync(ct);
        events.Track(AppEvents.TelegramLinked);
        await tg.SendTextAsync(chatId, $"✅ Підключено акаунт {user.DisplayName}.\n\n{HelpText}", ct: ct);
    }

    private async Task TodayAsync(long chatId, AppUser user, CancellationToken ct)
    {
        var from = AppTime.ToUtc(AppTime.NowLocal.Date);
        var meals = await db.Meals.AsNoTracking()
            .Where(m => m.UserId == user.Id && m.EatenAt >= from)
            .OrderBy(m => m.EatenAt)
            .Select(m => new { m.Type, m.EatenAt, m.DishName, m.Kcal, m.ProteinG, m.FatG, m.CarbsG })
            .ToListAsync(ct);

        var sb = new StringBuilder($"📒 Сьогодні, {AppTime.NowLocal:dd.MM}\n\n");
        if (meals.Count == 0) sb.AppendLine("Записів ще немає.");
        foreach (var m in meals)
            sb.AppendLine($"{AppTime.ToLocal(m.EatenAt):HH:mm} {MealTypeName(m.Type)}: {m.DishName ?? "без назви"}{(m.Kcal is { } k ? $" — {k} ккал" : "")}");

        var kcal = meals.Sum(m => m.Kcal ?? 0);
        sb.AppendLine()
          .AppendLine($"Разом: {kcal} з {user.DailyKcalGoal} ккал (лишилось {Math.Max(0, user.DailyKcalGoal - kcal)})")
          .Append($"Б {meals.Sum(m => m.ProteinG ?? 0):0} г · Ж {meals.Sum(m => m.FatG ?? 0):0} г · В {meals.Sum(m => m.CarbsG ?? 0):0} г");
        await tg.SendTextAsync(chatId, sb.ToString(), ct: ct);
    }

    private async Task PhotoAsync(long chatId, AppUser user, string fileId, CancellationToken ct)
    {
        await tg.SendTypingAsync(chatId, ct);
        try
        {
            var image = await tg.DownloadFileAsync(fileId, ct);
            var city = await cityContext.GetCurrentAsync();
            var a = await analysis.AnalyzeImageAsync(image, user.Id, city?.Id ?? 0, ct);
            if (!a.IsFood)
            {
                await tg.SendTextAsync(chatId, "Здається, на фото не їжа 🤔 Спробуйте інше фото.", ct: ct);
                return;
            }

            cache.Set(PendingKey(chatId), new PendingPhoto(image, a), PendingPhotoLifetime);
            var text = $"🍽 {a.DishName}\n{a.Kcal} ккал · Б {a.ProteinG:0} г · Ж {a.FatG:0} г · В {a.CarbsG:0} г\n" +
                       $"Корисність: {a.HealthScore}/10\n{a.Verdict}\n\nДодати в щоденник?";
            await tg.SendTextAsync(chatId, text,
            [
                new("🌅 Сніданок", "add:Breakfast"), new("🍲 Обід", "add:Lunch"),
                new("🌙 Вечеря", "add:Dinner"), new("🍎 Перекус", "add:Snack"),
                new("✖ Не додавати", "add:cancel"),
            ], ct);
        }
        catch (MealAnalysisException ex)
        {
            await tg.SendTextAsync(chatId, "⚠ " + ex.Message, ct: ct);
        }
    }

    private async Task HandleCallbackAsync(TgUpdate update, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.TelegramChatId == update.ChatId, ct);
        var data = update.CallbackData ?? "";
        if (user is null || !data.StartsWith("add:"))
        {
            await tg.AnswerCallbackAsync(update.CallbackId!, ct: ct);
            return;
        }

        if (!cache.TryGetValue(PendingKey(update.ChatId), out PendingPhoto? pending) || pending is null)
        {
            await tg.AnswerCallbackAsync(update.CallbackId!, "Фото вже неактуальне — надішліть його ще раз.", ct);
            return;
        }
        cache.Remove(PendingKey(update.ChatId));

        if (!Enum.TryParse<MealType>(data[4..], out var type))
        {
            await tg.AnswerCallbackAsync(update.CallbackId!, "Гаразд, не додаю.", ct);
            return;
        }

        var meal = new Meal { UserId = user.Id, Type = type, EatenAt = DateTime.UtcNow };
        meal.PhotoPath = await files.SaveImageAsync(ToFormFile(pending.Image), "meals", ct);
        await analysis.ApplyAsync(meal, pending.Analysis, ct);
        db.Meals.Add(meal);
        await db.SaveChangesAsync(ct);

        await tg.AnswerCallbackAsync(update.CallbackId!, "Додано ✅", ct);
        await tg.SendTextAsync(update.ChatId,
            $"✅ {meal.DishName} ({meal.Kcal} ккал) — {MealTypeName(type).ToLowerInvariant()}. {site.Value.BaseUrl}/Meals/Details/{meal.Id}", ct: ct);
    }

    private async Task AskAgentAsync(long chatId, AppUser user, string text, CancellationToken ct)
    {
        if (!agent.IsAvailable)
        {
            await tg.SendTextAsync(chatId, "AI-помічник зараз недоступний.", ct: ct);
            return;
        }
        var limits = await quota.GetAsync(user.Id, AiUsageKind.AgentMessage, ct);
        if (limits.Exhausted)
        {
            await tg.SendTextAsync(chatId, $"На плані Free — {limits.Limit} повідомлень помічнику на день. Premium знімає обмеження.", ct: ct);
            return;
        }
        db.AiAnalysisLogs.Add(new AiAnalysisLog { UserId = user.Id, Kind = AiUsageKind.AgentMessage });
        await db.SaveChangesAsync(ct);
        events.Track(AppEvents.AgentMessage, ("channel", "telegram"));
        await tg.SendTypingAsync(chatId, ct);

        var historyKey = $"tg-history:{chatId}";
        var history = cache.Get<List<AgentHistoryItem>>(historyKey) ?? [];
        var city = await cityContext.GetCurrentAsync();
        var ctx = new AgentContext(user.Id, city?.Id ?? 0, city?.Name, null, null, null);

        var steps = new StringBuilder();
        var answer = new StringBuilder();
        string? error = null;
        await foreach (var e in agent.RunAsync(text, history, ctx, ct))
        {
            switch (e.Type)
            {
                case "result": steps.AppendLine($"{(e.Ok == true ? "✔" : "⚠")} {e.Text}"); break;
                case "delta": answer.Append(e.Text); break;
                case "error": error = e.Text; break;
            }
        }

        var reply = error is not null ? "⚠ " + error : ToPlainText(answer.ToString(), site.Value.BaseUrl);
        if (steps.Length > 0) reply = steps + "\n" + reply;
        await tg.SendTextAsync(chatId, Truncate(reply, 4000), ct: ct);

        if (error is null)
        {
            history.Add(new AgentHistoryItem("user", text));
            history.Add(new AgentHistoryItem("assistant", answer.ToString()));
            cache.Set(historyKey, history.TakeLast(ChatHistorySize).ToList(), TimeSpan.FromHours(1));
        }
        logger.LogInformation("Telegram agent reply for chat {Chat}: {Length} chars", chatId, reply.Length);
    }

    public static string ToPlainText(string markdown, string baseUrl)
    {
        var text = MarkdownLink().Replace(markdown, m =>
            m.Groups[2].Value.StartsWith('/') ? $"{m.Groups[1].Value} ({baseUrl.TrimEnd('/')}{m.Groups[2].Value})" : $"{m.Groups[1].Value} ({m.Groups[2].Value})");
        text = text.Replace("**", "");
        return BulletLine().Replace(text, "• ");
    }

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"(?m)^\s*[*-]\s+")]
    private static partial Regex BulletLine();

    private static string PendingKey(long chatId) => $"tg-photo:{chatId}";

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private static string MealTypeName(MealType t) => t switch
    {
        MealType.Breakfast => "Сніданок", MealType.Lunch => "Обід", MealType.Dinner => "Вечеря", _ => "Перекус",
    };

    private static IFormFile ToFormFile(byte[] bytes) =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "photo", "photo.jpg")
        {
            Headers = new HeaderDictionary(), ContentType = ImageValidator.DetectMediaType(bytes) ?? "image/jpeg",
        };
}

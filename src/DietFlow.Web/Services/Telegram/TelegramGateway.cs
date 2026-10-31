using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace DietFlow.Web.Services.Telegram;

public class TelegramOptions
{
    public string? BotToken { get; set; }
    public string? BotUsername { get; set; }
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BotToken);
}

public record TgUpdate(long ChatId, string? Text = null, string? PhotoFileId = null, string? CallbackId = null, string? CallbackData = null);

public record TgButton(string Text, string Data);

public interface ITelegramGateway
{
    Task SendTextAsync(long chatId, string text, IReadOnlyList<TgButton>? buttons = null, CancellationToken ct = default);
    Task SendTypingAsync(long chatId, CancellationToken ct = default);
    Task<byte[]> DownloadFileAsync(string fileId, CancellationToken ct = default);
    Task AnswerCallbackAsync(string callbackId, string? text = null, CancellationToken ct = default);
}

public class TelegramBotGateway(ITelegramBotClient bot) : ITelegramGateway
{
    public Task SendTextAsync(long chatId, string text, IReadOnlyList<TgButton>? buttons = null, CancellationToken ct = default) =>
        bot.SendMessage(chatId, text,
            replyMarkup: buttons is { Count: > 0 }
                ? new InlineKeyboardMarkup(buttons.Chunk(2).Select(row => row.Select(b => InlineKeyboardButton.WithCallbackData(b.Text, b.Data))))
                : null,
            linkPreviewOptions: new() { IsDisabled = true },
            cancellationToken: ct);

    public Task SendTypingAsync(long chatId, CancellationToken ct = default) =>
        bot.SendChatAction(chatId, ChatAction.Typing, cancellationToken: ct);

    public async Task<byte[]> DownloadFileAsync(string fileId, CancellationToken ct = default)
    {
        var file = await bot.GetFile(fileId, ct);
        using var ms = new MemoryStream();
        await bot.DownloadFile(file, ms, ct);
        return ms.ToArray();
    }

    public Task AnswerCallbackAsync(string callbackId, string? text = null, CancellationToken ct = default) =>
        bot.AnswerCallbackQuery(callbackId, text, cancellationToken: ct);
}

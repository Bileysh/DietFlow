using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace DietFlow.Web.Services.Telegram;

public class TelegramPollingService(
    ITelegramBotClient bot, IServiceScopeFactory scopes, IOptions<TelegramOptions> options, ILogger<TelegramPollingService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.IsConfigured) return;

        await bot.DeleteWebhook(cancellationToken: stoppingToken);
        var me = await bot.GetMe(stoppingToken);
        logger.LogInformation("Telegram bot @{Username} started (long polling)", me.Username);

        int? offset = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await bot.GetUpdates(offset, timeout: 30,
                    allowedUpdates: [UpdateType.Message, UpdateType.CallbackQuery], cancellationToken: stoppingToken);
                foreach (var u in updates)
                {
                    offset = u.Id + 1;
                    var update = u switch
                    {
                        { Message: { } m } => new TgUpdate(m.Chat.Id, m.Text ?? m.Caption, m.Photo?.MaxBy(p => p.FileSize)?.FileId),
                        { CallbackQuery: { Message: { } m } q } => new TgUpdate(m.Chat.Id, CallbackId: q.Id, CallbackData: q.Data),
                        _ => null,
                    };
                    if (update is null) continue;

                    using var scope = scopes.CreateScope();
                    try
                    {
                        await scope.ServiceProvider.GetRequiredService<TelegramBotHandler>().HandleAsync(update, stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogError(ex, "Telegram update {Id} failed", u.Id);
                        await scope.ServiceProvider.GetRequiredService<ITelegramGateway>()
                            .SendTextAsync(update.ChatId, "⚠ Щось пішло не так. Спробуйте ще раз.", ct: stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Telegram polling error");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}

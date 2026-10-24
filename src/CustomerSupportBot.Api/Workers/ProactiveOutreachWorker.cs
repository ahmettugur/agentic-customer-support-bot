using CustomerSupportBot.Application.Services.Notifications;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Workers;

/// <summary>
/// Hosting adapter — proaktif geciken-kargo taramasını periyodik çalıştırır. İş mantığı
/// <see cref="ProactiveOutreachService"/> içinde; kapalıyken (varsayılan) döngü yalnızca uyur.
/// </summary>
public sealed class ProactiveOutreachWorker(
    ProactiveOutreachService service,
    IOptionsMonitor<ProactiveOutreachOptions> options,
    ILogger<ProactiveOutreachWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("[Proactive] Geciken kargo taraması {State}", options.CurrentValue.Enabled ? "açık" : "kapalı");

        while (!stoppingToken.IsCancellationRequested)
        {
            if (options.CurrentValue.Enabled)
            {
                try { await service.RunOnceAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.LogError(ex, "[Proactive] Tarama başarısız"); }
            }

            try { await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, options.CurrentValue.PollIntervalMinutes)), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}

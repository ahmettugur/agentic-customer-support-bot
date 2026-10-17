using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Workers;

/// <summary>
/// Hosting adapter — çökme/deploy yüzünden yarım kalan workflow turlarını periyodik arar ve bitirir.
/// İş mantığı <see cref="IWorkflowRecoveryPort"/> içinde (Adapters.Agents). Çok pod'da her tur için kilit alınır,
/// bu yüzden iki pod aynı turu devam ettirmez. Kapalıyken (varsayılan) döngü yalnızca uyur.
///
/// <para>
/// Port <b>tembel</b> çözülür (yalnızca özellik açıkken, ilk taramada): ardında CustomerSupportTeam → IChatClient zinciri var
/// ve LLM istemcisi anahtar yoksa kurulurken hata verir; işçi host açılışında bu zinciri tetiklememeli.
/// </para>
/// </summary>
public sealed class WorkflowRecoveryWorker(
    IServiceProvider services,
    IOptionsMonitor<WorkflowCheckpointingOptions> options,
    ILogger<WorkflowRecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("[Recovery] Workflow devam taraması {State}", options.CurrentValue.Enabled ? "açık" : "kapalı");

        while (!stoppingToken.IsCancellationRequested)
        {
            if (options.CurrentValue.Enabled)
            {
                try { await services.GetRequiredService<IWorkflowRecoveryPort>().RecoverStaleTurnsAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.LogError(ex, "[Recovery] Devam taraması başarısız"); }
            }

            try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, options.CurrentValue.SweepIntervalSeconds)), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}

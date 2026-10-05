using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Notifications;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Api.Workers;

/// <summary>
/// Hosting adapter — onay kararı olayını (<see cref="IApprovalQueue.RequestDecided"/>) e-posta bildirimine
/// bağlar. İş mantığı <see cref="ApprovalResultEmailService"/>'te (Application). Olay her pod'da tetiklenir;
/// tek gönderimi servis, bildirim defteriyle sağlar.
///
/// <para>
/// İşleyici kararı veren akışı beklemez ve onu hiçbir zaman bozmaz: gönderim arka planda yapılır, hata
/// loglanır.
/// </para>
/// </summary>
public sealed class ApprovalEmailNotificationService(
    IApprovalQueue approvals,
    ApprovalResultEmailService email,
    ILogger<ApprovalEmailNotificationService> logger) : IHostedService
{
    private readonly CancellationTokenSource _stopping = new();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        approvals.RequestDecided += OnDecided;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        approvals.RequestDecided -= OnDecided;
        _stopping.Cancel();
        return Task.CompletedTask;
    }

    private void OnDecided(object? sender, ApprovalRequest request)
    {
        if (_stopping.IsCancellationRequested) return;
        _ = Task.Run(async () =>
        {
            try { await email.HandleDecidedAsync(request, _stopping.Token); }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
            catch (Exception ex) { logger.LogError(ex, "[Email] Onay sonucu bildirimi işlenemedi | approval={Id}", request.Id); }
        });
    }
}

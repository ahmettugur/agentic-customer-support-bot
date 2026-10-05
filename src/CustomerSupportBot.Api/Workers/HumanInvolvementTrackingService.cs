using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Telemetry;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Api.Workers;

/// <summary>
/// Hosting adapter — eskalasyon oluşturma ve temsilci devralma olaylarını
/// <see cref="HumanInvolvementTracker"/>'a bağlar (yapay zekâ çözüm oranı). İşleyiciler olay akışını
/// beklemez; işaretleme arka planda yapılır, hata loglanır.
/// </summary>
public sealed class HumanInvolvementTrackingService(
    IEscalationSink escalations,
    IChatModeRegistry modes,
    HumanInvolvementTracker tracker,
    ILogger<HumanInvolvementTrackingService> logger) : IHostedService
{
    private readonly CancellationTokenSource _stopping = new();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        escalations.RequestCreated += OnEscalationCreated;
        modes.ModeChanged += OnModeChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        escalations.RequestCreated -= OnEscalationCreated;
        modes.ModeChanged -= OnModeChanged;
        _stopping.Cancel();
        return Task.CompletedTask;
    }

    private void OnEscalationCreated(object? sender, EscalationRequest e) => Mark(e.SessionId);

    private void OnModeChanged(object? sender, ChatSessionState s)
    {
        if (s.Mode == ChatMode.Human) Mark(s.SessionId);
    }

    private void Mark(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || _stopping.IsCancellationRequested) return;
        _ = Task.Run(async () =>
        {
            try { await tracker.MarkAsync(sessionId, _stopping.Token); }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
            catch (Exception ex) { logger.LogWarning(ex, "[Analytics] Görüşme 'insan dahil' olarak işaretlenemedi | session={Session}", sessionId); }
        });
    }
}

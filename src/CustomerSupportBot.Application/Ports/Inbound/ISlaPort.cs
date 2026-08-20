using CustomerSupportBot.Application.Services.Sla;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Inbound;

public sealed record SlaApprovalStatus(
    int PendingCount,
    int OldestSeconds,
    int WarnAfterSeconds,
    int BreachAfterSeconds,
    string OnBreach,
    int BreachCountRecent);

public sealed record SlaEscalationStatus(
    int OpenCount,
    int OldestSeconds,
    int WarnAfterSeconds,
    int BreachAfterSeconds,
    bool BoostPriorityOnBreach,
    int BreachCountRecent);

public sealed record SlaStatusResult(
    bool Enabled,
    int PollIntervalSeconds,
    SlaApprovalStatus Approvals,
    SlaEscalationStatus Escalations);

/// <summary>
/// SLA olay listesi, anlık durum özeti ve periyodik tarama için primary (driving) port.
/// </summary>
public interface ISlaPort
{
    IReadOnlyList<SlaEvent> GetRecentEvents(int count = 100);
    Task<SlaStatusResult> GetStatusAsync(CancellationToken ct = default);

    /// <summary>Tek bir SLA tarama döngüsünü çalıştırır; arka plan worker'ı tarafından her iterasyonda çağrılır.</summary>
    Task ScanOnceAsync(SlaOptions opts, CancellationToken ct = default);
}

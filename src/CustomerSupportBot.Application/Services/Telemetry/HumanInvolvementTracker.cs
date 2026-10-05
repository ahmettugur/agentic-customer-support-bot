// Application/Services/Telemetry/HumanInvolvementTracker.cs
// Görüşmeye insan dahil olduğunda (eskalasyon / devralma) oturumu işaretler — containment ölçümü için.

using CustomerSupportBot.Application.Ports.Outbound.Persistence;

namespace CustomerSupportBot.Application.Services.Telemetry;

/// <summary>
/// Eskalasyon oluşturulduğunda ve temsilci devraldığında çağrılır (barındırma: Api'deki
/// <c>HumanInvolvementTrackingService</c>). Olaylar pub/sub ile her pod'da tetiklenir; bayrak zaten
/// konmuşsa yazma yapılmaz, dolayısıyla tekrar zararsızdır.
/// </summary>
public sealed class HumanInvolvementTracker(ISessionManager sessions)
{
    public async Task MarkAsync(string? sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        var session = await sessions.GetAsync(sessionId, ct);
        if (session is null || session.State.HumanInvolved) return;

        await sessions.MutateStateAsync(sessionId, state =>
        {
            state.HumanInvolved = true;
            state.HumanInvolvedAt ??= DateTime.UtcNow;
        }, ct);
    }
}

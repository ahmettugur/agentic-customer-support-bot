// Ports/Driving/IEscalationPort.cs
// PRIMARY PORT — Eskalasyon yönetimi.
// AdminEndpoints eskalasyonları listeler ve karar verir.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Driving;

/// <summary>
/// Eskalasyon yönetimi için primary port.
/// </summary>
public interface IEscalationPort
{
    /// <summary>Yeni eskalasyon kaydı oluşturur.</summary>
    EscalationRequest Create(EscalationRequest request);

    /// <summary>Açık eskalasyonlar.</summary>
    IReadOnlyList<EscalationRequest> GetOpen();

    /// <summary>Son N eskalasyon.</summary>
    IReadOnlyList<EscalationRequest> GetRecent(int count = 50);

    /// <summary>Tek eskalasyon kaydı.</summary>
    EscalationRequest? Get(string id);

    /// <summary>Admin kararı: "acknowledge", "resolve", "dismiss".</summary>
    bool Decide(string id, string action, string? assignedTo = null, string? resolution = null);

    event EventHandler<EscalationRequest>? RequestCreated;
    event EventHandler<EscalationRequest>? RequestDecided;
}

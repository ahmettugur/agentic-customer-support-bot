// Ports/Driven/Persistence/ISlaEventRepository.cs
// SECONDARY PORT — SLA olay kalıcılığı ve bildirimi.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Driven.Persistence;

/// <summary>
/// SLA Guardian'ın ürettiği warn/breach olayları için secondary port.
/// Adaptörler: PostgresSlaEventSink, InMemorySlaEventSink.
/// </summary>
public interface ISlaEventRepository
{
    /// <summary>Yeni bir SLA olayı kaydeder ve event yayar.</summary>
    void Record(SlaEvent evt);

    /// <summary>En son N olay (default 100).</summary>
    IReadOnlyList<SlaEvent> GetRecent(int count = 100);

    /// <summary>
    /// Belirli bir target+severity için en son ne zaman event yayınlandığını döner.
    /// Bu sayede her tarama döngüsünde tekrar tekrar event üretilmez.
    /// </summary>
    DateTime? LastEmittedAt(string kind, string targetId, string severity);

    /// <summary>Yeni event eklendiğinde fire eder (UI canlı bildirim için).</summary>
    event EventHandler<SlaEvent>? EventRecorded;
}

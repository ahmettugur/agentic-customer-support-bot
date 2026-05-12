// Services/IEscalationSink.cs
// HITL — Specialist reasoning'de "needs_escalation" status'u için gerçek
// Kuyruk sözleşmesi. Workflow sonrası hook bu interface'e yazar.

using CustomerSupportBot.Api.Models;

namespace CustomerSupportBot.Api.Services;

public interface IEscalationSink
{
    /// <summary>Yeni bir eskalasyon kaydı oluşturur.</summary>
    EscalationRequest Create(EscalationRequest request);

    /// <summary>Açık olan eskalasyonlar (admin UI için).</summary>
    IReadOnlyList<EscalationRequest> GetOpen();

    /// <summary>Son N eskalasyon.</summary>
    IReadOnlyList<EscalationRequest> GetRecent(int count = 50);

    /// <summary>Tek kayıt.</summary>
    EscalationRequest? Get(string id);

    /// <summary>
    /// Admin kararı: "acknowledge", "resolve", "dismiss".
    /// Geçersiz action veya bulunamayan id için false döner.
    /// </summary>
    bool Decide(string id, string action, string? assignedTo = null, string? resolution = null);

    /// <summary>Yeni kayıt oluşunca event fire — UI için.</summary>
    event EventHandler<EscalationRequest>? RequestCreated;

    /// <summary>
    /// Bir eskalasyon üzerinde karar verildiğinde (acknowledge/resolve/dismiss)
    /// Fire olur. Payload: karar sonrası güncel EscalationRequest.
    /// Pending handoff UI temizlemesi buna bağlı çalışır.
    /// </summary>
    event EventHandler<EscalationRequest>? RequestDecided;
}

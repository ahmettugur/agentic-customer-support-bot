// Services/Sla/ISlaEventSink.cs
// SLA Guardian'ın ürettiği warn/breach olaylarını saklayan in-memory store.
// Admin UI bu sink üzerinden son N olayı görüntüler.

using CustomerSupportBot.Models;

namespace CustomerSupportBot.Services.Sla;

public interface ISlaEventSink
{
    /// <summary>Yeni bir SLA olayı kaydeder ve event yayar.</summary>
    void Record(SlaEvent evt);

    /// <summary>En son N olay (default 100).</summary>
    IReadOnlyList<SlaEvent> GetRecent(int count = 100);

    /// <summary>
    /// Belirli bir target+severity için en son ne zaman event yayınlandığını
    /// döner. Bu sayede her tarama döngüsünde tekrar tekrar event üretilmez.
    /// </summary>
    DateTime? LastEmittedAt(string kind, string targetId, string severity);

    /// <summary>Yeni event eklendiğinde fire eder (UI canlı bildirim için).</summary>
    event EventHandler<SlaEvent>? EventRecorded;
}

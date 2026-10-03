using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound.Observability;

/// <summary>
/// Agent reasoning trace'lerinin gözlemlenebilirlik kaydı için secondary port.
/// </summary>
public interface IReasoningTraceStore
{
    /// <summary>
    /// Yeni bir trace açar ve iskeletini kalıcılaştırır. Her sohbet turunda çağrılır — bu yüzden
    /// asenkron: DB beklenirken thread-pool thread'i rehin tutulmaz.
    /// </summary>
    Task<ReasoningTrace> StartTraceAsync(string sessionId, string query);

    /// <summary>Yalnızca cache'i günceller (tur boyunca yüksek frekansta çağrılır, I/O yok).</summary>
    void Update(ReasoningTrace trace);

    /// <summary>
    /// Trace'i kapatır ve son hâlini kalıcılaştırır.
    ///
    /// <para>
    /// Bilinçli olarak <see cref="CancellationToken"/> ALMAZ: iptal/timeout/hata yollarında da
    /// çağrılır ve o anda turun token'ı çoğu zaman zaten iptal edilmiştir. Kapanış yazısı iptal
    /// edilseydi trace "başlatılmış ama kapatılmamış" kalırdı — tam da hata ayıklamanın en çok
    /// gerektiği turlarda.
    /// </para>
    /// </summary>
    Task CompleteAsync(string traceId, string? terminationReason = null, string? finalResponse = null, string? error = null);

    IReadOnlyList<ReasoningTrace> GetRecent(int count = 50);
    IReadOnlyList<ReasoningTrace> GetBySession(string sessionId);
    ReasoningTrace? Get(string traceId);
}

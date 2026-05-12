// Services/IReasoningTraceStore.cs
// Reasoning trace'lerini saklayan store arayüzü.
// In-memory implementasyonu default. İstenirse Elasticsearch/SQL versiyonu eklenebilir.

using CustomerSupportBot.Api.Models;

namespace CustomerSupportBot.Api.Services;

public interface IReasoningTraceStore
{
    /// <summary>Yeni bir trace başlat — TraceId dönülür.</summary>
    ReasoningTrace StartTrace(string sessionId, string userQuery);

    /// <summary>Mevcut trace'i güncelle (mutable referansla).</summary>
    void Update(ReasoningTrace trace);

    /// <summary>Trace'i tamamlandı olarak işaretle.</summary>
    void Complete(string traceId, string? terminationReason = null, string? finalResponse = null, string? error = null);

    /// <summary>Bir trace'i ID ile getir.</summary>
    ReasoningTrace? Get(string traceId);

    /// <summary>Son N trace'i getir (dashboard için).</summary>
    IReadOnlyList<ReasoningTrace> GetRecent(int count = 50);

    /// <summary>Belirli oturuma ait trace'leri getir.</summary>
    IReadOnlyList<ReasoningTrace> GetBySession(string sessionId);
}

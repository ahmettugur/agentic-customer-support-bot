// Ports/Outbound/Observability/ITurnUsageTracker.cs
// Bir sohbet turunda kimin (Reasoning, Planning, ajanlar) kaç token harcadığı — tur sonu özet logu için.

namespace CustomerSupportBot.Application.Ports.Outbound.Observability;

/// <summary>
/// Tur başında <see cref="Begin"/> ile açılır; LLM çağrıları görüşme kapsamından (<see cref="ILlmCallAttribution"/>)
/// bulunan turun kovasına etiketle yazılır; tur bitince <see cref="Complete"/> özeti verir. Thread-safe: paralel alt
/// görevler aynı kovaya yazar.
/// </summary>
public interface ITurnUsageTracker
{
    void Begin(string sessionId);

    /// <summary>Geçerli görüşme kapsamındaki turun kovasına yazar; kapsam ya da açık tur yoksa yok sayar.</summary>
    void Record(string label, long inputTokens, long outputTokens);

    /// <summary>Turu kapatır ve özetini döner; hiç çağrı kaydedilmediyse <c>null</c>.</summary>
    TurnUsageSummary? Complete(string sessionId);
}

public sealed record TurnUsageLine(string Label, long InputTokens, long OutputTokens, int Calls);

public sealed record TurnUsageSummary(IReadOnlyList<TurnUsageLine> Lines)
{
    public long TotalInput => Lines.Sum(l => l.InputTokens);
    public long TotalOutput => Lines.Sum(l => l.OutputTokens);
    public int TotalCalls => Lines.Sum(l => l.Calls);

    /// <summary>Tek satırlık log metni. Yalnızca sayılar ve ajan adları: kullanıcı metni ve tam oturum kimliği içermez.</summary>
    public string Format(string sessionId)
    {
        var shortId = sessionId.Length <= 8 ? sessionId : sessionId[..8];
        var parts = Lines.Select(l => $"{l.Label} in {l.InputTokens}/out {l.OutputTokens} ({l.Calls} çağrı)");
        return $"[Usage] oturum={shortId} toplam in {TotalInput}/out {TotalOutput} ({TotalCalls} çağrı) | {string.Join(" | ", parts)}";
    }
}

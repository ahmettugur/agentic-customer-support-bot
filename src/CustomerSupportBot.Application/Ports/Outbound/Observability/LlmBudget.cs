// Ports/Outbound/Observability/LlmBudget.cs
// LLM harcama limiti: kontrol/kayıt (ILlmSpendGuard) ve pod'lar arası sayaç (ILlmSpendCounter).

using System.Globalization;

namespace CustomerSupportBot.Application.Ports.Outbound.Observability;

public enum LlmBudgetScope { Daily, Monthly, Conversation }

public sealed record LlmBudgetExceeded(LlmBudgetScope Scope, decimal LimitUsd, decimal SpentUsd);

/// <summary>Limit aşıldığı için LLM çağrısı yapılmadı (bkz. <c>SpendLimitChatClient</c>).</summary>
public sealed class LlmBudgetExceededException(LlmBudgetExceeded detail)
    : Exception(string.Create(CultureInfo.InvariantCulture,
        $"LLM bütçesi aşıldı ({detail.Scope}: {detail.SpentUsd:F2} / {detail.LimitUsd:F2} USD)."))
{
    public LlmBudgetExceeded Detail { get; } = detail;
}

public sealed record LlmBudgetStatus(
    bool Enabled,
    decimal DailyLimitUsd,
    decimal DailySpentUsd,
    decimal MonthlyLimitUsd,
    decimal MonthlySpentUsd,
    decimal PerConversationLimitUsd,
    int WarningThresholdPercent);

/// <summary>
/// Harcama limiti. Yumuşak tavandır: kontrol çağrıdan önce, kayıt çağrıdan sonra yapılır (maliyet ancak
/// yanıtla bilinir). Sayaç hatası hizmeti düşürmez — kontrol açık kalır (fail-open).
/// </summary>
public interface ILlmSpendGuard
{
    /// <summary>Aşılmış ilk limit (gün → ay → görüşme); aşılmamışsa ya da kapalıysa <c>null</c>.</summary>
    Task<LlmBudgetExceeded?> CheckAsync(string? sessionId, CancellationToken ct = default);

    /// <summary>Bir çağrının maliyetini sayar; eşik geçilirse uyarır. Hata fırlatmaz.</summary>
    Task RecordAsync(decimal costUsd, string? sessionId, CancellationToken ct = default);

    Task<LlmBudgetStatus> GetStatusAsync(CancellationToken ct = default);
}

/// <summary>Pod'lar arası harcama sayacı (Redis). Anahtar ön eki adaptörde eklenir.</summary>
public interface ILlmSpendCounter
{
    /// <summary>Anahtar yoksa <c>null</c>.</summary>
    Task<decimal?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>Atomik artış; yeni toplamı döner. TTL yalnızca ilk yazımda konur (dönem sonunda düşer).</summary>
    Task<decimal> AddAsync(string key, decimal amount, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Anahtar yoksa değeri yazar (yoksa hiçbir şey yapmaz).</summary>
    Task SeedAsync(string key, decimal value, TimeSpan ttl, CancellationToken ct = default);
}

namespace CustomerSupportBot.Application.Ports.Outbound.Observability;

/// <summary>
/// LLM çağrı kayıtlarını kalıcı depolamaya yazan secondary port.
/// Implementasyon: PostgresLlmCallUsageSink (prod) veya no-op (InMemory/test).
/// </summary>
public interface ILlmCallPersistencePort
{
    /// <summary>
    /// Tek bir LLM çağrısını persist eder. Fire-and-forget çağrılabilir.
    /// Hata durumunda caller'a exception sızdırmaz.
    /// </summary>
    Task RecordAsync(LlmCallRecord record, CancellationToken ct = default);

    /// <summary>
    /// LLM maliyet özeti (<paramref name="sinceUtc"/>'den itibaren; null = tümü): toplam, görüşmeye atfedilen,
    /// görüşme başına ortalama ve medyan.
    /// </summary>
    Task<LlmCostSummary> GetCostSummaryAsync(DateTime? sinceUtc = null, CancellationToken ct = default);

    /// <summary><paramref name="sinceUtc"/>'den bu yana toplam maliyet (harcama sayacının tohumlanması için).</summary>
    Task<decimal> GetTotalCostSinceAsync(DateTime sinceUtc, CancellationToken ct = default);
}

/// <summary>Persist edilen LLM çağrı kaydı.</summary>
public sealed record LlmCallRecord(
    string Model,
    string Provider,
    long InputTokens,
    long OutputTokens,
    decimal CostUsd,
    double DurationMs,
    DateTime CalledAt,
    string? SessionId = null);

/// <param name="AttributedCostUsd">Bir görüşmeye atfedilen maliyet; kalan (toplam − atfedilen) arka plan işleridir.</param>
public sealed record LlmCostSummary(
    decimal TotalCostUsd,
    decimal AttributedCostUsd,
    int SessionsWithCost,
    decimal AverageCostPerSessionUsd,
    decimal MedianCostPerSessionUsd)
{
    public static LlmCostSummary Empty { get; } = new(0, 0, 0, 0, 0);
}

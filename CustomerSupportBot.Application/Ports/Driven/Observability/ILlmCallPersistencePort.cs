namespace CustomerSupportBot.Application.Ports.Driven.Observability;

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
}

/// <summary>Persist edilen LLM çağrı kaydı.</summary>
public sealed record LlmCallRecord(
    string Model,
    string Provider,
    long InputTokens,
    long OutputTokens,
    decimal CostUsd,
    double DurationMs,
    DateTime CalledAt);

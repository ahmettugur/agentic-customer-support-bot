namespace CustomerSupportBot.Application.Ports.Outbound.Observability;

public sealed record CostModelUsageSnapshot(
    string Model,
    long Calls,
    long InputTokens,
    long OutputTokens,
    decimal CostUsd,
    double AverageLatencyMs,
    DateTime LastUsed);

public sealed record CostUsageSnapshot(
    long TotalCalls,
    long TotalInputTokens,
    long TotalOutputTokens,
    decimal TotalCostUsd,
    IReadOnlyList<CostModelUsageSnapshot> ByModel);

/// <summary>
/// Toplam token/maliyet sayaçlarını okuyan secondary port.
/// </summary>
public interface ICostUsageStorePort
{
    CostUsageSnapshot GetUsageSnapshot();
    void ResetUsage();
}

// Services/Telemetry/CostUsageStore.cs
// In-memory toplam kullanım & maliyet sayaçları. OpenTelemetry meter'a yazılan
// veriyi paralel olarak burada da tutar; admin /telemetry/cost endpoint'i bu store'u
// okur. Production'da bu yerine Prometheus + Grafana ya da Application Insights
// query API kullanılabilir; ama "her şey hazır" demo değeri için lokal store
// pratik bir çözümdür.

using System.Collections.Concurrent;

namespace CustomerSupportBot.Services.Telemetry;

public sealed class CostUsageStore
{
    private readonly ConcurrentDictionary<string, ModelUsage> _byModel =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly object _lock = new();
    private long _totalInputTokens;
    private long _totalOutputTokens;
    private decimal _totalCost;
    private long _totalCalls;

    public void Record(string model, long inputTokens, long outputTokens, decimal costUsd, double durationMs)
    {
        if (string.IsNullOrWhiteSpace(model)) model = "(unknown)";

        _byModel.AddOrUpdate(model,
            _ => new ModelUsage
            {
                Model = model,
                Calls = 1,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                CostUsd = costUsd,
                AverageLatencyMs = durationMs,
                LastUsed = DateTime.UtcNow
            },
            (_, existing) =>
            {
                lock (existing)
                {
                    existing.Calls++;
                    existing.InputTokens += inputTokens;
                    existing.OutputTokens += outputTokens;
                    existing.CostUsd += costUsd;
                    // running mean
                    existing.AverageLatencyMs = existing.AverageLatencyMs +
                        (durationMs - existing.AverageLatencyMs) / existing.Calls;
                    existing.LastUsed = DateTime.UtcNow;
                    return existing;
                }
            });

        lock (_lock)
        {
            _totalInputTokens += inputTokens;
            _totalOutputTokens += outputTokens;
            _totalCost += costUsd;
            _totalCalls++;
        }
    }

    public CostSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return new CostSnapshot
            {
                TotalCalls = _totalCalls,
                TotalInputTokens = _totalInputTokens,
                TotalOutputTokens = _totalOutputTokens,
                TotalCostUsd = _totalCost,
                ByModel = _byModel.Values
                    .OrderByDescending(m => m.CostUsd)
                    .Select(m => new ModelUsage
                    {
                        Model = m.Model,
                        Calls = m.Calls,
                        InputTokens = m.InputTokens,
                        OutputTokens = m.OutputTokens,
                        CostUsd = Math.Round(m.CostUsd, 6),
                        AverageLatencyMs = Math.Round(m.AverageLatencyMs, 2),
                        LastUsed = m.LastUsed
                    })
                    .ToList()
            };
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _byModel.Clear();
            _totalInputTokens = 0;
            _totalOutputTokens = 0;
            _totalCost = 0;
            _totalCalls = 0;
        }
    }
}

public sealed class ModelUsage
{
    public string Model { get; set; } = "";
    public long Calls { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
    public double AverageLatencyMs { get; set; }
    public DateTime LastUsed { get; set; }
}

public sealed class CostSnapshot
{
    public long TotalCalls { get; set; }
    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }
    public decimal TotalCostUsd { get; set; }
    public List<ModelUsage> ByModel { get; set; } = new();
}

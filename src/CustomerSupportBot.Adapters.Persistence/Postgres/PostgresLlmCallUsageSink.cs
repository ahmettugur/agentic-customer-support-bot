using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

/// <summary>
/// ILlmCallPersistencePort implementasyonu — her LLM çağrısını PostgreSQL'e yazar.
/// TelemetryChatClient tarafından fire-and-forget olarak çağrılır; caller bloke olmaz.
/// </summary>
public sealed class PostgresLlmCallUsageSink : ILlmCallPersistencePort
{
    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;
    private readonly ILogger<PostgresLlmCallUsageSink> _logger;

    public PostgresLlmCallUsageSink(
        IDbContextFactory<CustomerSupportDbContext> dbFactory,
        ILogger<PostgresLlmCallUsageSink> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task RecordAsync(LlmCallRecord record, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            db.LlmCallUsages.Add(new LlmCallUsageEntity
            {
                Model = record.Model,
                Provider = record.Provider,
                InputTokens = record.InputTokens,
                OutputTokens = record.OutputTokens,
                CostUsd = record.CostUsd,
                DurationMs = record.DurationMs,
                CalledAt = record.CalledAt,
                SessionId = record.SessionId
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM çağrı kaydı Postgres'e yazılamadı (model={Model})", record.Model);
        }
    }

    public async Task<decimal> GetTotalCostSinceAsync(DateTime sinceUtc, CancellationToken ct = default)
    {
        var since = DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.LlmCallUsages.Where(r => r.CalledAt >= since).SumAsync(r => (decimal?)r.CostUsd, ct) ?? 0m;
    }

    public async Task<LlmCostSummary> GetCostSummaryAsync(DateTime? sinceUtc = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = db.LlmCallUsages.AsNoTracking();
        if (sinceUtc is { } since) rows = rows.Where(r => r.CalledAt >= since);

        var total = await rows.SumAsync(r => (decimal?)r.CostUsd, ct) ?? 0m;
        // Görüşme başına toplamlar DB'de gruplanır; ortalama ve medyan bellekte (görüşme sayısı kadar satır).
        var perSession = await rows
            .Where(r => r.SessionId != null)
            .GroupBy(r => r.SessionId)
            .Select(g => g.Sum(r => r.CostUsd))
            .ToListAsync(ct);

        if (perSession.Count == 0) return LlmCostSummary.Empty with { TotalCostUsd = total };

        perSession.Sort();
        var attributed = perSession.Sum();
        var mid = perSession.Count / 2;
        var median = perSession.Count % 2 == 1 ? perSession[mid] : (perSession[mid - 1] + perSession[mid]) / 2m;
        return new LlmCostSummary(total, attributed, perSession.Count, attributed / perSession.Count, median);
    }
}

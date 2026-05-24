using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Observability;
using CustomerSupportBot.Application.Ports.Driven.Observability;
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
                CalledAt = record.CalledAt
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM çağrı kaydı Postgres'e yazılamadı (model={Model})", record.Model);
        }
    }
}

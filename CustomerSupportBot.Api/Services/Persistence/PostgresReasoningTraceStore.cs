// Services/Persistence/PostgresReasoningTraceStore.cs
// Hibrit cache + PostgreSQL reasoning trace store.
//
// Yazma stratejisi (DB write-storm önlemi):
//   - StartTrace: cache + DB INSERT (skeleton: TraceId, SessionId, UserQuery, StartedAt).
//   - Update: SADECE cache. Workflow boyunca onlarca Update gelir, DB'ye yazılmaz.
//   - Complete: cache + DB UPDATE (tek seferde tam snapshot — JSONB sütunlar dahil).
//
// Trade-off: Process bir trace'i tamamlamadan crash olursa o trace DB'de
// "skeleton" halinde kalır. PersistenceHydrator startup'ta CompletedAt=null
// olan eski trace'leri Error="terminated_by_restart" olarak işaretler.
//
// Okuma stratejisi: Cache primary; cache miss → DB fallback (Get yalnız).

using System.Collections.Concurrent;
using System.Text.Json;
using CustomerSupportBot.Infrastructure.Persistence;
using CustomerSupportBot.Infrastructure.Persistence.Entities.Observability;
using CustomerSupportBot.Models;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Services.Persistence;

public sealed class PostgresReasoningTraceStore : IReasoningTraceStore
{
    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;
    private readonly ILogger<PostgresReasoningTraceStore> _logger;
    private readonly ConcurrentDictionary<string, ReasoningTrace> _byId = new();
    private readonly ConcurrentQueue<string> _insertionOrder = new();
    private readonly int _maxCacheCapacity;
    private readonly object _hydrationLock = new();
    private volatile bool _hydrated;

    public PostgresReasoningTraceStore(
        IDbContextFactory<CustomerSupportDbContext> dbFactory,
        ILogger<PostgresReasoningTraceStore> logger,
        int maxCacheCapacity = 500)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _maxCacheCapacity = maxCacheCapacity;
    }

    public ReasoningTrace StartTrace(string sessionId, string userQuery)
    {
        EnsureHydrated();

        var trace = new ReasoningTrace
        {
            SessionId = sessionId,
            UserQuery = userQuery,
            StartedAt = DateTime.UtcNow
        };

        _byId[trace.TraceId] = trace;
        _insertionOrder.Enqueue(trace.TraceId);
        TrimCache();

        try { InsertSkeletonAsync(trace).GetAwaiter().GetResult(); }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Trace] Skeleton INSERT başarısız. TraceId={TraceId}", trace.TraceId);
            // Cache'te tut — workflow kesintisiz devam etsin.
        }

        return trace;
    }

    public void Update(ReasoningTrace trace)
    {
        // Sadece cache. Workflow yüksek frekansta Update çağırır.
        _byId[trace.TraceId] = trace;
    }

    public void Complete(string traceId, string? terminationReason = null, string? finalResponse = null, string? error = null)
    {
        if (!_byId.TryGetValue(traceId, out var trace)) return;

        trace.CompletedAt = DateTime.UtcNow;
        if (terminationReason != null) trace.TerminationReason = terminationReason;
        if (finalResponse != null)
        {
            trace.FinalResponse = finalResponse.Length > 2000
                ? finalResponse[..2000] + "…"
                : finalResponse;
        }
        if (error != null) trace.Error = error;

        try { UpdateFullAsync(trace).GetAwaiter().GetResult(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Trace] Complete UPDATE başarısız. TraceId={TraceId}", traceId);
        }
    }

    public ReasoningTrace? Get(string traceId)
    {
        if (_byId.TryGetValue(traceId, out var t)) return t;

        // Cache miss → DB fallback (eski trace'ler).
        try
        {
            using var ctx = _dbFactory.CreateDbContext();
            var e = ctx.ReasoningTraces.AsNoTracking().FirstOrDefault(x => x.TraceId == traceId);
            return e is null ? null : ToDomain(e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Trace] DB Get başarısız. TraceId={TraceId}", traceId);
            return null;
        }
    }

    public IReadOnlyList<ReasoningTrace> GetRecent(int count = 50)
    {
        EnsureHydrated();
        return _byId.Values
            .OrderByDescending(t => t.StartedAt)
            .Take(count)
            .ToList();
    }

    public IReadOnlyList<ReasoningTrace> GetBySession(string sessionId)
    {
        EnsureHydrated();
        return _byId.Values
            .Where(t => t.SessionId == sessionId)
            .OrderByDescending(t => t.StartedAt)
            .ToList();
    }

    /// <summary>
    /// PersistenceHydrator startup'ta çağırır — yarım kalmış trace'leri Error işaretler.
    /// </summary>
    internal async Task MarkInflightAsErrorOnStartupAsync(CancellationToken ct = default)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        var orphans = await ctx.ReasoningTraces
            .Where(t => t.CompletedAt == null)
            .ToListAsync(ct);

        if (orphans.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var t in orphans)
        {
            t.CompletedAt = now;
            t.TerminationReason = "process_restart";
            t.Error = "terminated_by_restart";
        }

        await ctx.SaveChangesAsync(ct);
        _logger.LogWarning(
            "[Trace] {Count} in-flight trace process_restart olarak işaretlendi.",
            orphans.Count);
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task InsertSkeletonAsync(ReasoningTrace t)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        ctx.ReasoningTraces.Add(new ReasoningTraceEntity
        {
            TraceId = t.TraceId,
            SessionId = t.SessionId,
            UserQuery = t.UserQuery,
            StartedAt = t.StartedAt,
            IterationCount = 0,
            EstimatedTokens = 0,
            WasRevised = false,
            SpecialistReasoningsJson = "[]",
            AgentVisitsJson = "[]",
            ToolCallsJson = "[]"
        });
        await ctx.SaveChangesAsync();
    }

    private async Task UpdateFullAsync(ReasoningTrace t)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        var existing = await ctx.ReasoningTraces.FirstOrDefaultAsync(x => x.TraceId == t.TraceId);
        if (existing is null)
        {
            ctx.ReasoningTraces.Add(ToEntity(t));
        }
        else
        {
            existing.CompletedAt = t.CompletedAt;
            existing.TerminationReason = t.TerminationReason;
            existing.FinalResponse = t.FinalResponse;
            existing.IterationCount = t.IterationCount;
            existing.Error = t.Error;
            existing.EstimatedTokens = t.EstimatedTokens;
            existing.ReasoningJson = Serialize(t.Reasoning);
            existing.PlanningJson = Serialize(t.Planning);
            existing.SpecialistReasoningsJson = Serialize(t.SpecialistReasonings) ?? "[]";
            existing.AgentVisitsJson = Serialize(t.AgentVisits) ?? "[]";
            existing.ToolCallsJson = Serialize(t.ToolCalls) ?? "[]";
        }
        await ctx.SaveChangesAsync();
    }

    private static ReasoningTraceEntity ToEntity(ReasoningTrace t) => new()
    {
        TraceId = t.TraceId,
        SessionId = t.SessionId,
        UserQuery = t.UserQuery,
        StartedAt = t.StartedAt,
        CompletedAt = t.CompletedAt,
        TerminationReason = t.TerminationReason,
        FinalResponse = t.FinalResponse,
        IterationCount = t.IterationCount,
        Error = t.Error,
        EstimatedTokens = t.EstimatedTokens,
        ReasoningJson = Serialize(t.Reasoning),
        PlanningJson = Serialize(t.Planning),
        SpecialistReasoningsJson = Serialize(t.SpecialistReasonings) ?? "[]",
        AgentVisitsJson = Serialize(t.AgentVisits) ?? "[]",
        ToolCallsJson = Serialize(t.ToolCalls) ?? "[]"
    };

    private static ReasoningTrace ToDomain(ReasoningTraceEntity e) => new()
    {
        TraceId = e.TraceId,
        SessionId = e.SessionId,
        UserQuery = e.UserQuery,
        StartedAt = e.StartedAt,
        CompletedAt = e.CompletedAt,
        TerminationReason = e.TerminationReason,
        FinalResponse = e.FinalResponse,
        IterationCount = e.IterationCount,
        Error = e.Error,
        EstimatedTokens = e.EstimatedTokens,
        Reasoning = Deserialize<ReasoningResult>(e.ReasoningJson),
        Planning = Deserialize<PlanningResult>(e.PlanningJson),
        SpecialistReasonings = Deserialize<List<SpecialistReasoning>>(e.SpecialistReasoningsJson) ?? new(),
        AgentVisits = Deserialize<List<AgentVisit>>(e.AgentVisitsJson) ?? new(),
        ToolCalls = Deserialize<List<ToolInvocation>>(e.ToolCallsJson) ?? new()
    };

    private static string? Serialize<T>(T? value)
        where T : class
        => value is null ? null : JsonSerializer.Serialize(value);

    private static T? Deserialize<T>(string? json)
        where T : class
        => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<T>(json);

    private void TrimCache()
    {
        while (_insertionOrder.Count > _maxCacheCapacity && _insertionOrder.TryDequeue(out var oldId))
        {
            _byId.TryRemove(oldId, out _);
        }
    }

    private void EnsureHydrated()
    {
        if (_hydrated) return;
        lock (_hydrationLock)
        {
            if (_hydrated) return;
            try
            {
                HydrateAsync().GetAwaiter().GetResult();
                _hydrated = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Trace] Cache hydrate başarısız.");
            }
        }
    }

    private async Task HydrateAsync()
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        var rows = await ctx.ReasoningTraces.AsNoTracking()
            .OrderByDescending(t => t.StartedAt)
            .Take(_maxCacheCapacity)
            .ToListAsync();

        // Eskiden yeniye doğru ekle ki insertion order doğru olsun.
        foreach (var e in rows.OrderBy(t => t.StartedAt))
        {
            var domain = ToDomain(e);
            _byId[domain.TraceId] = domain;
            _insertionOrder.Enqueue(domain.TraceId);
        }

        _logger.LogInformation("[Trace] Cache hydrate: {Count} trace", rows.Count);
    }
}

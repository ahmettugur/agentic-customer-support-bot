// Services/Persistence/PostgresLessonStore.cs
// Hibrit Lesson store — in-memory cache + PostgreSQL write-through.
// Singleton servis ⇒ DbContext'i IDbContextFactory üzerinden açar.
//
// Yatay ölçeklendirme (Redis pub/sub):
//   - Lesson kaydı küçük/sınırlı olduğu için Add/Update sonrası TAM kayıt yayınlanır.

using System.Collections.Concurrent;
using System.Text.Json;
using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Improvement;
using CustomerSupportBot.Application.Ports.Outbound.Messaging;
using CustomerSupportBot.Domain.Model.Improvement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

public sealed class PostgresLessonStore : ILessonStore
{
    private const string ChannelUpserted = "csbot:lesson:upserted";

    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;
    private readonly ILogger<PostgresLessonStore> _logger;
    private readonly IMessageBusPort _messageBus;
    private readonly ConcurrentDictionary<string, Lesson> _cache = new();
    private readonly object _hydrationLock = new();
    private volatile bool _hydrated;

    private static readonly JsonSerializerOptions _json = new();

    public PostgresLessonStore(
        IDbContextFactory<CustomerSupportDbContext> dbFactory,
        IMessageBusPort messageBus,
        ILogger<PostgresLessonStore> logger)
    {
        _dbFactory = dbFactory;
        _messageBus = messageBus;
        _logger = logger;
        _messageBus.Subscribe(ChannelUpserted, OnRemoteUpserted);
    }

    public void Add(Lesson lesson)
    {
        try
        {
            UpsertAsync(lesson).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Lesson] DB INSERT başarısız. Id={Id}", lesson.Id);
            throw;
        }
        _cache[lesson.Id] = lesson;
        PublishUpserted(lesson);
    }

    public Lesson? Get(string id)
    {
        EnsureHydrated();
        return _cache.TryGetValue(id, out var l) ? l : null;
    }

    public void Update(Lesson lesson)
    {
        try
        {
            UpsertAsync(lesson).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Lesson] DB UPDATE başarısız. Id={Id}", lesson.Id);
            throw;
        }
        _cache[lesson.Id] = lesson;
        PublishUpserted(lesson);
    }

    public IReadOnlyList<Lesson> GetByStatus(LessonStatus status)
    {
        EnsureHydrated();
        return _cache.Values
            .Where(l => l.Status == status)
            .OrderByDescending(l => l.CreatedAt)
            .ToList();
    }

    public IReadOnlyList<Lesson> GetAll(int limit = 200)
    {
        EnsureHydrated();
        return _cache.Values
            .OrderByDescending(l => l.CreatedAt)
            .Take(limit)
            .ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task UpsertAsync(Lesson lesson)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();

        var entity = await ctx.Lessons.FirstOrDefaultAsync(e => e.Id == lesson.Id);
        var mapped = ToEntity(lesson);

        if (entity is null)
        {
            ctx.Lessons.Add(mapped);
        }
        else
        {
            entity.Title = mapped.Title;
            entity.LessonText = mapped.LessonText;
            entity.Observation = mapped.Observation;
            entity.SuggestedAgent = mapped.SuggestedAgent;
            entity.SourceTraceIdsJson = mapped.SourceTraceIdsJson;
            entity.Status = mapped.Status;
            entity.DecidedBy = mapped.DecidedBy;
            entity.DecidedAt = mapped.DecidedAt;
            entity.DecisionReason = mapped.DecisionReason;
            entity.VectorMemoryId = mapped.VectorMemoryId;
        }

        await ctx.SaveChangesAsync();
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
                _logger.LogError(ex, "[Lesson] Cache hydrate başarısız.");
            }
        }
    }

    private async Task HydrateAsync()
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        var rows = await ctx.Lessons.AsNoTracking().ToListAsync();
        foreach (var e in rows)
            _cache[e.Id] = ToDomain(e);

        _logger.LogInformation("[Lesson] Cache hydrate tamam: {Count} kayıt", rows.Count);
    }

    private static LessonEntity ToEntity(Lesson l) => new()
    {
        Id = l.Id,
        Title = l.Title,
        LessonText = l.LessonText,
        Observation = l.Observation,
        SuggestedAgent = l.SuggestedAgent,
        SourceTraceIdsJson = JsonSerializer.Serialize(l.SourceTraceIds, _json),
        Status = l.Status.ToString(),
        DecidedBy = l.DecidedBy,
        DecidedAt = l.DecidedAt,
        DecisionReason = l.DecisionReason,
        CreatedAt = l.CreatedAt == default ? DateTime.UtcNow : l.CreatedAt,
        VectorMemoryId = l.VectorMemoryId
    };

    private static Lesson ToDomain(LessonEntity e) => new()
    {
        Id = e.Id,
        Title = e.Title,
        LessonText = e.LessonText,
        Observation = e.Observation,
        SuggestedAgent = e.SuggestedAgent,
        SourceTraceIds = JsonSerializer.Deserialize<List<string>>(e.SourceTraceIdsJson, _json) ?? new(),
        Status = Enum.TryParse<LessonStatus>(e.Status, out var s) ? s : LessonStatus.Proposed,
        DecidedBy = e.DecidedBy,
        DecidedAt = e.DecidedAt,
        DecisionReason = e.DecisionReason,
        CreatedAt = e.CreatedAt,
        VectorMemoryId = e.VectorMemoryId
    };

    // ─── Redis cross-pod handlers ─────────────────────────────────────────────

    private void PublishUpserted(Lesson lesson)
    {
        var payload = new { nodeId = _messageBus.NodeId, lesson };
        _messageBus.Publish(ChannelUpserted, JsonSerializer.Serialize(payload, _json));
    }

    private void OnRemoteUpserted(string val)
    {
        try
        {
            using var doc = JsonDocument.Parse(val);
            var root = doc.RootElement;
            if (root.GetProperty("nodeId").GetString() == _messageBus.NodeId) return;

            var lesson = JsonSerializer.Deserialize<Lesson>(root.GetProperty("lesson").GetRawText(), _json);
            if (lesson is null) return;

            _cache[lesson.Id] = lesson;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Lesson] Redis OnRemoteUpserted parse hatası");
        }
    }
}


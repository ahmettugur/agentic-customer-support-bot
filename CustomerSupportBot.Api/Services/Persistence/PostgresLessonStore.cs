// Services/Persistence/PostgresLessonStore.cs
// Hibrit Lesson store — in-memory cache + PostgreSQL write-through.
// Singleton servis ⇒ DbContext'i IDbContextFactory üzerinden açar.

using System.Collections.Concurrent;
using System.Text.Json;
using CustomerSupportBot.Api.Infrastructure.Persistence;
using CustomerSupportBot.Api.Infrastructure.Persistence.Entities.Improvement;
using CustomerSupportBot.Api.Models.Improvement;
using CustomerSupportBot.Api.Services.Improvement;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Api.Services.Persistence;

public sealed class PostgresLessonStore : ILessonStore
{
    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;
    private readonly ILogger<PostgresLessonStore> _logger;
    private readonly ConcurrentDictionary<string, Lesson> _cache = new();
    private readonly object _hydrationLock = new();
    private volatile bool _hydrated;

    private static readonly JsonSerializerOptions _json = new();

    public PostgresLessonStore(
        IDbContextFactory<CustomerSupportDbContext> dbFactory,
        ILogger<PostgresLessonStore> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
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
}

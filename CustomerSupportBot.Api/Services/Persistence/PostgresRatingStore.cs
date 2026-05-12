// Services/Persistence/PostgresRatingStore.cs
// Hibrit Rating store — in-memory cache + PostgreSQL write-through.
//
// Davranış:
//   - Submit(): önce DB'ye UPSERT, ardından cache güncellenir (durable-first).
//   - GetBySession/All/Recent: cache'den okur. Cache "soğuk" ise (henüz hydrate
//     edilmemişse) tek seferlik DB'den tüm tablo yüklenir.
//   - Singleton servis ⇒ DbContext'i IDbContextFactory üzerinden açar.
//
// Bu store cache'i yetkili kabul eder; DB sadece dayanıklılık için. Restart'ta
// PersistenceHydrator (Faz 2 sonu) önceden hydrate eder.

using System.Collections.Concurrent;
using CustomerSupportBot.Infrastructure.Persistence;
using CustomerSupportBot.Infrastructure.Persistence.Entities.Analytics;
using CustomerSupportBot.Models;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Services.Persistence;

public sealed class PostgresRatingStore : IRatingStore
{
    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;
    private readonly ILogger<PostgresRatingStore> _logger;
    private readonly ConcurrentDictionary<string, ConversationRating> _cache = new();
    private readonly object _hydrationLock = new();
    private volatile bool _hydrated;

    public PostgresRatingStore(
        IDbContextFactory<CustomerSupportDbContext> dbFactory,
        ILogger<PostgresRatingStore> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public ConversationRating Submit(string sessionId, int stars, string? feedback)
    {
        EnsureHydrated();

        var rating = new ConversationRating
        {
            SessionId = sessionId,
            Stars = Math.Clamp(stars, 1, 5),
            Feedback = feedback?.Trim(),
            RatedAt = DateTime.UtcNow
        };

        // 1) DB write-through (UPSERT) — sync wrap (Singleton + sync interface).
        try
        {
            UpsertAsync(rating).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Rating] DB UPSERT başarısız. SessionId={SessionId}", sessionId);
            throw;
        }

        // 2) Cache update.
        _cache.AddOrUpdate(sessionId, rating, (_, _) => rating);

        _logger.LogInformation(
            "[Rating] Session {SessionId} rated {Stars} stars. Feedback: {Feedback}",
            sessionId, rating.Stars, rating.Feedback ?? "(yok)");

        return rating;
    }

    public ConversationRating? GetBySession(string sessionId)
    {
        EnsureHydrated();
        return _cache.TryGetValue(sessionId, out var r) ? r : null;
    }

    public IReadOnlyList<ConversationRating> GetAll()
    {
        EnsureHydrated();
        return _cache.Values
            .OrderByDescending(r => r.RatedAt)
            .ToList();
    }

    public IReadOnlyList<ConversationRating> GetRecent(int count = 20)
    {
        EnsureHydrated();
        return _cache.Values
            .OrderByDescending(r => r.RatedAt)
            .Take(count)
            .ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task UpsertAsync(ConversationRating rating)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();

        var existing = await ctx.Ratings
            .FirstOrDefaultAsync(r => r.SessionId == rating.SessionId);

        if (existing is null)
        {
            ctx.Ratings.Add(new RatingEntity
            {
                SessionId = rating.SessionId,
                Id = rating.Id,
                Stars = rating.Stars,
                Feedback = rating.Feedback,
                RatedAt = rating.RatedAt
            });
        }
        else
        {
            existing.Id = rating.Id;
            existing.Stars = rating.Stars;
            existing.Feedback = rating.Feedback;
            existing.RatedAt = rating.RatedAt;
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
                // Cache hydrate edilemese bile yazma yolu çalışmaya devam eder.
                _logger.LogError(ex, "[Rating] Cache hydrate başarısız.");
            }
        }
    }

    private async Task HydrateAsync()
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        var rows = await ctx.Ratings.AsNoTracking().ToListAsync();

        foreach (var e in rows)
        {
            _cache[e.SessionId] = new ConversationRating
            {
                Id = e.Id,
                SessionId = e.SessionId,
                Stars = e.Stars,
                Feedback = e.Feedback,
                RatedAt = e.RatedAt
            };
        }

        _logger.LogInformation("[Rating] Cache hydrate tamam: {Count} kayıt", rows.Count);
    }
}

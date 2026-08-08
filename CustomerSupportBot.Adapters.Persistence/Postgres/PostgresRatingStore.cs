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
//
// Yatay ölçeklendirme (Redis pub/sub):
//   - Rating kaydı küçük/sınırlı olduğu için Submit() sonrası TAM kayıt yayınlanır.

using System.Collections.Concurrent;
using System.Text.Json;
using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Analytics;
using CustomerSupportBot.Application.Ports.Outbound.Messaging;
using CustomerSupportBot.Domain.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

public sealed class PostgresRatingStore : IRatingStore
{
    private const string ChannelSubmitted = "csbot:rating:submitted";

    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;
    private readonly ILogger<PostgresRatingStore> _logger;
    private readonly IMessageBusPort _messageBus;
    private readonly ConcurrentDictionary<string, ConversationRating> _cache = new();
    private readonly object _hydrationLock = new();
    private volatile bool _hydrated;

    public PostgresRatingStore(
        IDbContextFactory<CustomerSupportDbContext> dbFactory,
        IMessageBusPort messageBus,
        ILogger<PostgresRatingStore> logger)
    {
        _dbFactory = dbFactory;
        _messageBus = messageBus;
        _logger = logger;
        _messageBus.Subscribe(ChannelSubmitted, OnRemoteSubmitted);
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
        PublishSubmitted(rating);

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

    // ─── Redis cross-pod handlers ─────────────────────────────────────────────

    private void PublishSubmitted(ConversationRating rating)
    {
        var payload = new { nodeId = _messageBus.NodeId, rating };
        _messageBus.Publish(ChannelSubmitted, JsonSerializer.Serialize(payload));
    }

    private void OnRemoteSubmitted(string val)
    {
        try
        {
            using var doc = JsonDocument.Parse(val);
            var root = doc.RootElement;
            if (root.GetProperty("nodeId").GetString() == _messageBus.NodeId) return;

            var rating = JsonSerializer.Deserialize<ConversationRating>(root.GetProperty("rating").GetRawText());
            if (rating is null) return;

            _cache[rating.SessionId] = rating;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Rating] Redis OnRemoteSubmitted parse hatası");
        }
    }
}


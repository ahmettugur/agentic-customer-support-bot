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

public sealed class PostgresRatingStore : IRatingStore, ICacheWarmup, ISessionDataEraser
{
    private const string ChannelSubmitted = "csbot:rating:submitted";

    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;
    private readonly ILogger<PostgresRatingStore> _logger;
    private readonly IMessageBusPort _messageBus;
    private readonly ConcurrentDictionary<string, ConversationRating> _cache = new();
    private readonly SemaphoreSlim _hydrationGate = new(1, 1);
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
        PrivacyChannels.SubscribeSessionsErased(_messageBus, EvictSessions, _logger);
    }

    // ─── Kişisel veri silme (ISessionDataEraser) ───

    public string Name => "ratings";

    /// <summary>Oturumların puan ve yorumlarını siler (yorum serbest metindir, kişisel veri içerebilir).</summary>
    public async Task<int> EraseSessionsAsync(IReadOnlyCollection<string> sessionIds, CancellationToken ct = default)
    {
        if (sessionIds.Count == 0) return 0;
        var ids = sessionIds.ToList();
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var deleted = await db.Ratings.Where(r => ids.Contains(r.SessionId)).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        EvictSessions(ids.ToHashSet(StringComparer.Ordinal));
        PrivacyChannels.PublishSessionsErased(_messageBus, ids);
        return deleted;
    }

    private void EvictSessions(IReadOnlySet<string> sessionIds)
    {
        foreach (var id in sessionIds) _cache.TryRemove(id, out _);
    }

    public async Task<ConversationRating> SubmitAsync(string sessionId, int stars, string? feedback)
{
        await EnsureHydratedAsync().ConfigureAwait(false);

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
            await UpsertAsync(rating).ConfigureAwait(false);
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

        // Yorumun KENDİSİ loglanmaz: serbest metindir (müşteri telefon/adres yazabilir) ve
        // log, kişisel verinin saklanması için tasarlanmış bir yüzey değildir. Yorum zaten
        // DB'de; log yalnızca olayın varlığını ve boyutunu taşır.
        _logger.LogInformation(
            "[Rating] Session {SessionId} rated {Stars} stars. FeedbackLength={FeedbackLength}",
            sessionId, rating.Stars, rating.Feedback?.Length ?? 0);

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

    /// <summary>
    /// Session başına tek puan (session_id birincil anahtar). Oku-sonra-yaz atomik değil:
    /// aynı oturuma eşzamanlı İLK iki puanlamanın ikisi de "kayıt yok" görüp INSERT eder ve
    /// kaybeden birincil anahtar ihlaliyle 500 alırdı. Kaybeden artık bir kez, kazananın
    /// satırını güncelleyerek tekrar dener (son yazan kazanır — tekrar puanlamayla aynı anlam).
    /// </summary>
    private async Task UpsertAsync(ConversationRating rating)
    {
        try
        {
            await UpsertOnceAsync(rating).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException
                                           { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            await UpsertOnceAsync(rating).ConfigureAwait(false);
        }
    }

    private async Task UpsertOnceAsync(ConversationRating rating)
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

    /// <inheritdoc />
    public Task WarmUpAsync(CancellationToken ct = default) => EnsureHydratedAsync();

    /// <summary>
    /// Senkron okuma yolları için YEDEK. Normalde cache açılışta <see cref="WarmUpAsync"/> ile
    /// doldurulmuştur ve bu çağrı bayrağı okuyup hemen döner; yalnızca ısıtma başarısız
    /// olduysa ilk okuma hydrate'i senkron bekler.
    /// </summary>
    private void EnsureHydrated()
    {
        if (_hydrated) return;
        EnsureHydratedAsync().GetAwaiter().GetResult();
    }

    private async Task EnsureHydratedAsync()
    {
        if (_hydrated) return;
        await _hydrationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_hydrated) return;
            await HydrateAsync().ConfigureAwait(false);
            _hydrated = true;
        }
        catch (Exception ex)
        {
            // Cache hydrate edilemese bile yazma yolu çalışmaya devam eder.
            _logger.LogError(ex, "[Rating] Cache hydrate başarısız.");
        }
        finally
        {
            _hydrationGate.Release();
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


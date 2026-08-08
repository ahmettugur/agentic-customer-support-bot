// Services/Persistence/PostgresCustomerProfileStore.cs
// Hibrit CustomerProfile store — in-memory cache + PostgreSQL write-through.
// Singleton servis ⇒ DbContext'i IDbContextFactory üzerinden açar.
//
// Yatay ölçeklendirme (Redis pub/sub):
//   - Profil kaydı küçük/sınırlı olduğu için Upsert/Delete sonrası TAM kayıt yayınlanır.

using System.Collections.Concurrent;
using System.Text.Json;
using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Personalization;
using CustomerSupportBot.Application.Ports.Outbound.Messaging;
using CustomerSupportBot.Domain.Model.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

public sealed class PostgresCustomerProfileStore : ICustomerProfileStore
{
    private const string ChannelUpserted = "csbot:customerprofile:upserted";
    private const string ChannelDeleted = "csbot:customerprofile:deleted";

    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;
    private readonly ILogger<PostgresCustomerProfileStore> _logger;
    private readonly IMessageBusPort _messageBus;
    private readonly ConcurrentDictionary<string, CustomerProfile> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _hydrationLock = new();
    private volatile bool _hydrated;

    private static readonly JsonSerializerOptions _json = new();

    public PostgresCustomerProfileStore(
        IDbContextFactory<CustomerSupportDbContext> dbFactory,
        IMessageBusPort messageBus,
        ILogger<PostgresCustomerProfileStore> logger)
    {
        _dbFactory = dbFactory;
        _messageBus = messageBus;
        _logger = logger;
        _messageBus.Subscribe(ChannelUpserted, OnRemoteUpserted);
        _messageBus.Subscribe(ChannelDeleted, OnRemoteDeleted);
    }

    public CustomerProfile? Get(string customerId)
    {
        if (string.IsNullOrWhiteSpace(customerId)) return null;
        EnsureHydrated();
        return _cache.TryGetValue(customerId, out var p) ? p : null;
    }

    public CustomerProfile GetOrCreate(string customerId)
    {
        if (string.IsNullOrWhiteSpace(customerId))
            throw new ArgumentException("customerId boş olamaz", nameof(customerId));

        EnsureHydrated();

        if (_cache.TryGetValue(customerId, out var existing)) return existing;

        var profile = new CustomerProfile { CustomerId = customerId };
        Upsert(profile);
        return profile;
    }

    public void Upsert(CustomerProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.CustomerId))
            throw new ArgumentException("CustomerId boş olamaz", nameof(profile));

        if (profile.LastInteractionAt == default)
            profile.LastInteractionAt = DateTime.UtcNow;

        try
        {
            UpsertAsync(profile).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[CustomerProfile] DB UPSERT başarısız. CustomerId={CustomerId}", profile.CustomerId);
            throw;
        }

        _cache[profile.CustomerId] = profile;
        PublishUpserted(profile);
    }

    public bool Delete(string customerId)
    {
        if (string.IsNullOrWhiteSpace(customerId)) return false;
        EnsureHydrated();

        try
        {
            DeleteAsync(customerId).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[CustomerProfile] DB DELETE başarısız. CustomerId={CustomerId}", customerId);
        }

        var removed = _cache.TryRemove(customerId, out _);
        PublishDeleted(customerId);
        return removed;
    }

    public IReadOnlyList<CustomerProfile> List(int take = 100)
    {
        EnsureHydrated();
        if (take <= 0) take = 100;
        return _cache.Values
            .OrderByDescending(p => p.LastInteractionAt)
            .Take(take)
            .ToList();
    }

    public int Count
    {
        get
        {
            EnsureHydrated();
            return _cache.Count;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task UpsertAsync(CustomerProfile p)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();

        var entity = await ctx.CustomerProfiles
            .FirstOrDefaultAsync(e => e.CustomerId == p.CustomerId);

        var mapped = ToEntity(p);

        if (entity is null)
        {
            ctx.CustomerProfiles.Add(mapped);
        }
        else
        {
            entity.PreferredLanguage = mapped.PreferredLanguage;
            entity.PreferredTone = mapped.PreferredTone;
            entity.IntentFrequencyJson = mapped.IntentFrequencyJson;
            entity.ProductInterestsJson = mapped.ProductInterestsJson;
            entity.RecentRatingsJson = mapped.RecentRatingsJson;
            entity.Summary = mapped.Summary;
            entity.AdminNote = mapped.AdminNote;
            entity.TotalSessions = mapped.TotalSessions;
            entity.TotalTurns = mapped.TotalTurns;
            entity.LastInteractionAt = mapped.LastInteractionAt;
            entity.LastConsolidatedAt = mapped.LastConsolidatedAt;
        }

        await ctx.SaveChangesAsync();
    }

    private async Task DeleteAsync(string customerId)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        var entity = await ctx.CustomerProfiles
            .FirstOrDefaultAsync(e => e.CustomerId == customerId);
        if (entity != null)
        {
            ctx.CustomerProfiles.Remove(entity);
            await ctx.SaveChangesAsync();
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
                _logger.LogError(ex, "[CustomerProfile] Cache hydrate başarısız.");
            }
        }
    }

    private async Task HydrateAsync()
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        var rows = await ctx.CustomerProfiles.AsNoTracking().ToListAsync();
        foreach (var e in rows)
            _cache[e.CustomerId] = ToDomain(e);

        _logger.LogInformation("[CustomerProfile] Cache hydrate tamam: {Count} kayıt", rows.Count);
    }

    private static CustomerProfileEntity ToEntity(CustomerProfile p) => new()
    {
        CustomerId = p.CustomerId,
        PreferredLanguage = p.PreferredLanguage,
        PreferredTone = p.PreferredTone,
        IntentFrequencyJson = JsonSerializer.Serialize(p.IntentFrequency, _json),
        ProductInterestsJson = JsonSerializer.Serialize(p.ProductInterests, _json),
        RecentRatingsJson = JsonSerializer.Serialize(p.RecentRatings, _json),
        Summary = p.Summary,
        AdminNote = p.AdminNote,
        TotalSessions = p.TotalSessions,
        TotalTurns = p.TotalTurns,
        CreatedAt = p.CreatedAt == default ? DateTime.UtcNow : p.CreatedAt,
        LastInteractionAt = p.LastInteractionAt,
        LastConsolidatedAt = p.LastConsolidatedAt
    };

    private static CustomerProfile ToDomain(CustomerProfileEntity e) => new()
    {
        CustomerId = e.CustomerId,
        PreferredLanguage = e.PreferredLanguage,
        PreferredTone = e.PreferredTone,
        IntentFrequency = JsonSerializer.Deserialize<Dictionary<string, int>>(e.IntentFrequencyJson, _json) ?? new(),
        ProductInterests = JsonSerializer.Deserialize<List<string>>(e.ProductInterestsJson, _json) ?? new(),
        RecentRatings = JsonSerializer.Deserialize<List<int>>(e.RecentRatingsJson, _json) ?? new(),
        Summary = e.Summary,
        AdminNote = e.AdminNote,
        TotalSessions = e.TotalSessions,
        TotalTurns = e.TotalTurns,
        CreatedAt = e.CreatedAt,
        LastInteractionAt = e.LastInteractionAt,
        LastConsolidatedAt = e.LastConsolidatedAt
    };

    // ─── Redis cross-pod handlers ─────────────────────────────────────────────

    private void PublishUpserted(CustomerProfile profile)
    {
        var payload = new { nodeId = _messageBus.NodeId, profile };
        _messageBus.Publish(ChannelUpserted, JsonSerializer.Serialize(payload, _json));
    }

    private void OnRemoteUpserted(string val)
    {
        try
        {
            using var doc = JsonDocument.Parse(val);
            var root = doc.RootElement;
            if (root.GetProperty("nodeId").GetString() == _messageBus.NodeId) return;

            var profile = JsonSerializer.Deserialize<CustomerProfile>(
                root.GetProperty("profile").GetRawText(), _json);
            if (profile is null) return;

            _cache[profile.CustomerId] = profile;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[CustomerProfile] Redis OnRemoteUpserted parse hatası");
        }
    }

    private void PublishDeleted(string customerId)
    {
        var payload = new { nodeId = _messageBus.NodeId, customerId };
        _messageBus.Publish(ChannelDeleted, JsonSerializer.Serialize(payload));
    }

    private void OnRemoteDeleted(string val)
    {
        try
        {
            using var doc = JsonDocument.Parse(val);
            var root = doc.RootElement;
            if (root.GetProperty("nodeId").GetString() == _messageBus.NodeId) return;

            var customerId = root.GetProperty("customerId").GetString();
            if (customerId is null) return;

            _cache.TryRemove(customerId, out _);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[CustomerProfile] Redis OnRemoteDeleted parse hatası");
        }
    }
}


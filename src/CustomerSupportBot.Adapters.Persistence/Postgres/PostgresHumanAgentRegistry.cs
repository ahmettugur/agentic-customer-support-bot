// Services/Persistence/PostgresHumanAgentRegistry.cs
// Hibrit cache + PostgreSQL human agent kayıt deposu.
// IHumanAgentRegistry implementasyonu — InMemoryHumanAgentRegistry'nin DB-backed karşılığı.
//
// Davranış:
//   - Lazy hydrate: ilk read'de tüm kayıtlar DB'den cache'e çekilir.
//   - Create/Update/Delete: DB + cache senkron güncellenir.
//   - IncrementLoad/DecrementLoad: cache üzerinden per-agent lock + DB UPDATE.
//
// Yatay ölçeklendirme (Redis pub/sub):
//   - HumanAgent kaydı küçük/sınırlı olduğu için (ChatModeRegistry'deki gibi) her
//     değişiklikte TAM kayıt yayınlanır — delta gerekmiyor.
//   - Create/Update/IncrementLoad/DecrementLoad → csbot:humanagent:upserted.
//   - Delete → csbot:humanagent:deleted.
//   - SetPresence/TouchPresence → csbot:humanagent:presence — YALNIZCA durum alanları. Tam kayıt
//     yayınlansaydı her 30 sn'lik kalp atışı, yayınlayan pod'un (mesaj kaçırmışsa eski) yük sayacını
//     tüm pod'lara yayardı. Aynı nedenle durum yazımı DB'de yalnızca durum sütunlarını günceller ve
//     tam kayıt yayını (upserted) alınırken daha yeni durum bilgisi korunur.

using System.Collections.Concurrent;
using System.Text.Json;
using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Hitl;
using CustomerSupportBot.Application.Ports.Outbound.Messaging;
using CustomerSupportBot.Domain.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

public sealed class PostgresHumanAgentRegistry : IHumanAgentRegistry
{
    private const string ChannelUpserted = "csbot:humanagent:upserted";
    private const string ChannelDeleted = "csbot:humanagent:deleted";
    private const string ChannelPresence = "csbot:humanagent:presence";

    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;
    private readonly ILogger<PostgresHumanAgentRegistry> _logger;
    private readonly IMessageBusPort _messageBus;
    private readonly ConcurrentDictionary<string, HumanAgent> _agents = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _hydrationLock = new();
    private volatile bool _hydrated;

    public PostgresHumanAgentRegistry(
        IDbContextFactory<CustomerSupportDbContext> dbFactory,
        IMessageBusPort messageBus,
        ILogger<PostgresHumanAgentRegistry> logger)
    {
        _dbFactory = dbFactory;
        _messageBus = messageBus;
        _logger = logger;
        _messageBus.Subscribe(ChannelUpserted, OnRemoteUpserted);
        _messageBus.Subscribe(ChannelDeleted, OnRemoteDeleted);
        _messageBus.Subscribe(ChannelPresence, OnRemotePresence);
    }

    // ─── IHumanAgentRegistry ───

    public IReadOnlyList<HumanAgent> GetAll()
    {
        EnsureHydrated();
        return _agents.Values
            .OrderByDescending(a => a.IsActive)
            .ThenBy(a => a.DisplayName)
            .ToList();
    }

    public IReadOnlyList<HumanAgent> GetActive()
    {
        EnsureHydrated();
        return _agents.Values.Where(a => a.IsActive).ToList();
    }

    public HumanAgent? Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        EnsureHydrated();
        return _agents.TryGetValue(id, out var a) ? a : null;
    }

    public HumanAgent Create(HumanAgent agent)
    {
        EnsureHydrated();

        if (string.IsNullOrWhiteSpace(agent.Id))
            agent.Id = Guid.NewGuid().ToString("N")[..8];
        agent.Skills = NormalizeTags(agent.Skills);
        agent.Languages = NormalizeTags(agent.Languages);
        agent.CreatedAt = agent.CreatedAt == default ? DateTime.UtcNow : agent.CreatedAt;
        if (agent.MaxConcurrentLoad <= 0) agent.MaxConcurrentLoad = 5;

        UpsertToDb(agent);
        _agents[agent.Id] = agent;
        PublishUpserted(agent);
        return agent;
    }

    public HumanAgent? Update(string id, HumanAgentInput input)
    {
        EnsureHydrated();
        if (!_agents.TryGetValue(id, out var existing)) return null;

        if (!string.IsNullOrWhiteSpace(input.DisplayName))
            existing.DisplayName = input.DisplayName.Trim();
        if (input.Email != null)
            existing.Email = string.IsNullOrWhiteSpace(input.Email) ? null : input.Email.Trim();
        if (input.Skills != null)
            existing.Skills = NormalizeTags(input.Skills);
        if (input.Languages != null)
            existing.Languages = NormalizeTags(input.Languages);
        if (input.IsActive.HasValue)
            existing.IsActive = input.IsActive.Value;
        if (input.MaxConcurrentLoad.HasValue && input.MaxConcurrentLoad.Value > 0)
            existing.MaxConcurrentLoad = input.MaxConcurrentLoad.Value;
        if (input.Priority.HasValue)
            existing.Priority = input.Priority.Value;

        UpsertToDb(existing);
        PublishUpserted(existing);
        return existing;
    }

    public bool Delete(string id)
    {
        EnsureHydrated();
        if (!_agents.TryRemove(id, out _)) return false;

        try
        {
            using var ctx = _dbFactory.CreateDbContext();
            var entity = ctx.HumanAgents.Find(id);
            if (entity != null)
            {
                ctx.HumanAgents.Remove(entity);
                ctx.SaveChanges();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Routing] HumanAgent DELETE başarısız. Id={Id}", id);
        }
        PublishDeleted(id);
        return true;
    }

    public bool IncrementLoad(string id)
    {
        EnsureHydrated();
        if (!_agents.TryGetValue(id, out var a)) return false;
        lock (a)
        {
            a.CurrentLoad = Math.Min(a.CurrentLoad + 1, int.MaxValue);
            a.LastAssignedAt = DateTime.UtcNow;
        }
        UpdateLoadInDb(id, a.CurrentLoad, a.LastAssignedAt);
        PublishUpserted(a);
        return true;
    }

    public bool DecrementLoad(string id)
    {
        EnsureHydrated();
        if (!_agents.TryGetValue(id, out var a)) return false;
        lock (a)
        {
            a.CurrentLoad = Math.Max(a.CurrentLoad - 1, 0);
        }
        UpdateLoadInDb(id, a.CurrentLoad, a.LastAssignedAt);
        PublishUpserted(a);
        return true;
    }

    public bool SetPresence(string id, AgentPresence presence, DateTime nowUtc)
    {
        EnsureHydrated();
        if (!_agents.TryGetValue(id, out var a)) return false;
        lock (a)
        {
            if (a.Presence != presence || a.PresenceChangedAt is null) a.PresenceChangedAt = nowUtc;
            a.Presence = presence;
            a.LastSeenAt = nowUtc;
        }
        WritePresence(a);
        return true;
    }

    public bool TouchPresence(string id, DateTime nowUtc)
    {
        EnsureHydrated();
        if (!_agents.TryGetValue(id, out var a)) return false;
        lock (a) a.LastSeenAt = nowUtc;
        WritePresence(a);
        return true;
    }

    public async Task<IReadOnlyList<HumanAgent>> GetLinkedUsersAsync(CancellationToken ct = default)
    {
        try
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
            return await ctx.Users
                .Where(u => u.Role == "Agent" && u.LinkedAgentId != null && u.IsActive)
                .OrderBy(u => u.Username)
                .Select(u => new HumanAgent
                {
                    Id = u.LinkedAgentId!,
                    DisplayName = u.Username,
                    IsActive = u.IsActive
                })
                .ToListAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Routing] GetLinkedUsersAsync başarısız");
            return new List<HumanAgent>();
        }
    }

    // ─── Hydration ───

    private void EnsureHydrated()
    {
        if (_hydrated) return;
        lock (_hydrationLock)
        {
            if (_hydrated) return;
            try
            {
                using var ctx = _dbFactory.CreateDbContext();
                var entities = ctx.HumanAgents.AsNoTracking().ToList();
                foreach (var e in entities)
                    _agents[e.Id] = MapToModel(e);

                _logger.LogInformation("[Routing] {Count} human agent DB'den yüklendi", entities.Count);
                // _hydrated yalnızca başarıdan sonra set edilir — aksi halde geçici bir DB
                // hatası bu registry'yi process ömrü boyunca "hydrate edildi ama boş" bırakır
                // ve bir sonraki çağrı DB'yi tekrar denemeden geçer.
                _hydrated = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Routing] HumanAgent hydration başarısız");
            }
        }
    }

    // ─── DB Helpers ───

    private void UpsertToDb(HumanAgent agent)
    {
        try
        {
            using var ctx = _dbFactory.CreateDbContext();
            var existing = ctx.HumanAgents.Find(agent.Id);
            if (existing != null)
            {
                existing.DisplayName = agent.DisplayName;
                existing.Email = agent.Email;
                existing.SkillsJson = JsonSerializer.Serialize(agent.Skills);
                existing.LanguagesJson = JsonSerializer.Serialize(agent.Languages);
                existing.IsActive = agent.IsActive;
                existing.MaxConcurrentLoad = agent.MaxConcurrentLoad;
                existing.CurrentLoad = agent.CurrentLoad;
                existing.Priority = agent.Priority;
                existing.CreatedAt = agent.CreatedAt;
                existing.LastAssignedAt = agent.LastAssignedAt;
                // Durum sütunları burada yazılmaz: yönetici güncellemesi, bu pod'un önbelleğindeki
                // (eski olabilecek) durumla temsilcinin güncel seçimini ezmesin. Bkz. WritePresence.
            }
            else
            {
                ctx.HumanAgents.Add(MapToEntity(agent));
            }
            ctx.SaveChanges();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Routing] HumanAgent UPSERT başarısız. Id={Id}", agent.Id);
        }
    }

    private void UpdateLoadInDb(string id, int currentLoad, DateTime? lastAssignedAt)
    {
        try
        {
            using var ctx = _dbFactory.CreateDbContext();
            ctx.HumanAgents
                .Where(e => e.Id == id)
                .ExecuteUpdate(s => s
                    .SetProperty(e => e.CurrentLoad, currentLoad)
                    .SetProperty(e => e.LastAssignedAt, lastAssignedAt));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Routing] HumanAgent load UPDATE başarısız. Id={Id}", id);
        }
    }

    private void WritePresence(HumanAgent a)
    {
        AgentPresence presence; DateTime? since, lastSeen;
        lock (a) (presence, since, lastSeen) = (a.Presence, a.PresenceChangedAt, a.LastSeenAt);
        try
        {
            using var ctx = _dbFactory.CreateDbContext();
            ctx.HumanAgents
                .Where(e => e.Id == a.Id)
                .ExecuteUpdate(s => s
                    .SetProperty(e => e.Presence, presence.ToString())
                    .SetProperty(e => e.PresenceChangedAt, since)
                    .SetProperty(e => e.LastSeenAt, lastSeen));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Routing] HumanAgent presence UPDATE başarısız. Id={Id}", a.Id);
        }
        var payload = new { nodeId = _messageBus.NodeId, id = a.Id, presence, since, lastSeen };
        _messageBus.Publish(ChannelPresence, JsonSerializer.Serialize(payload));
    }

    // ─── Mapping ───

    private static HumanAgent MapToModel(HumanAgentEntity e) => new()
    {
        Id = e.Id,
        DisplayName = e.DisplayName,
        Email = e.Email,
        Skills = DeserializeList(e.SkillsJson),
        Languages = DeserializeList(e.LanguagesJson),
        IsActive = e.IsActive,
        MaxConcurrentLoad = e.MaxConcurrentLoad,
        CurrentLoad = e.CurrentLoad,
        Priority = e.Priority,
        CreatedAt = e.CreatedAt,
        LastAssignedAt = e.LastAssignedAt,
        Presence = Enum.TryParse<AgentPresence>(e.Presence, ignoreCase: true, out var p) ? p : AgentPresence.Offline,
        PresenceChangedAt = e.PresenceChangedAt,
        LastSeenAt = e.LastSeenAt
    };

    private static HumanAgentEntity MapToEntity(HumanAgent a) => new()
    {
        Id = a.Id,
        DisplayName = a.DisplayName,
        Email = a.Email,
        SkillsJson = JsonSerializer.Serialize(a.Skills),
        LanguagesJson = JsonSerializer.Serialize(a.Languages),
        IsActive = a.IsActive,
        MaxConcurrentLoad = a.MaxConcurrentLoad,
        CurrentLoad = a.CurrentLoad,
        Priority = a.Priority,
        CreatedAt = a.CreatedAt,
        LastAssignedAt = a.LastAssignedAt,
        Presence = a.Presence.ToString(),
        PresenceChangedAt = a.PresenceChangedAt,
        LastSeenAt = a.LastSeenAt
    };

    private static List<string> DeserializeList(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch { return new(); }
    }

    private static List<string> NormalizeTags(List<string>? input)
    {
        if (input == null || input.Count == 0) return new();
        return input
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    // ─── Redis cross-pod handlers ─────────────────────────────────────────────

    private void PublishUpserted(HumanAgent agent)
    {
        var payload = new { nodeId = _messageBus.NodeId, agent };
        _messageBus.Publish(ChannelUpserted, JsonSerializer.Serialize(payload));
    }

    private void OnRemoteUpserted(string val)
    {
        try
        {
            using var doc = JsonDocument.Parse(val);
            var root = doc.RootElement;
            if (root.GetProperty("nodeId").GetString() == _messageBus.NodeId) return;

            var agent = JsonSerializer.Deserialize<HumanAgent>(root.GetProperty("agent").GetRawText());
            if (agent is null) return;

            // Durum ayrı kanalla yayılır; yayınlayan pod'un durum bilgisi eskiyse yereldeki korunur.
            if (_agents.TryGetValue(agent.Id, out var local))
            {
                lock (local)
                {
                    if (Newer(local.PresenceChangedAt, agent.PresenceChangedAt))
                        (agent.Presence, agent.PresenceChangedAt) = (local.Presence, local.PresenceChangedAt);
                    if (Newer(local.LastSeenAt, agent.LastSeenAt))
                        agent.LastSeenAt = local.LastSeenAt;
                }
            }
            _agents[agent.Id] = agent;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Routing] Redis OnRemoteUpserted parse hatası");
        }
    }

    private static bool Newer(DateTime? a, DateTime? b) => a is { } x && (b is not { } y || x > y);

    private void OnRemotePresence(string val)
    {
        try
        {
            using var doc = JsonDocument.Parse(val);
            var root = doc.RootElement;
            if (root.GetProperty("nodeId").GetString() == _messageBus.NodeId) return;

            var id = root.GetProperty("id").GetString();
            if (id is null || !_agents.TryGetValue(id, out var a)) return;

            var presence = root.GetProperty("presence").Deserialize<AgentPresence>();
            var since = root.GetProperty("since").Deserialize<DateTime?>();
            var lastSeen = root.GetProperty("lastSeen").Deserialize<DateTime?>();
            lock (a)
            {
                // İki sekme/pod'dan neredeyse aynı anda gelen seçimlerde sıra karışabilir: yalnızca daha
                // eski olmayan bilgi uygulanır.
                if (!Newer(a.PresenceChangedAt, since)) (a.Presence, a.PresenceChangedAt) = (presence, since);
                if (!Newer(a.LastSeenAt, lastSeen)) a.LastSeenAt = lastSeen;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Routing] Redis OnRemotePresence parse hatası");
        }
    }

    private void PublishDeleted(string id)
    {
        var payload = new { nodeId = _messageBus.NodeId, id };
        _messageBus.Publish(ChannelDeleted, JsonSerializer.Serialize(payload));
    }

    private void OnRemoteDeleted(string val)
    {
        try
        {
            using var doc = JsonDocument.Parse(val);
            var root = doc.RootElement;
            if (root.GetProperty("nodeId").GetString() == _messageBus.NodeId) return;

            var id = root.GetProperty("id").GetString();
            if (id is null) return;

            _agents.TryRemove(id, out _);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Routing] Redis OnRemoteDeleted parse hatası");
        }
    }
}


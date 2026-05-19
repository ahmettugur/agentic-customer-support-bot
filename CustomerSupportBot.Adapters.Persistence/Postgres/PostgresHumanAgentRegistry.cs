// Services/Persistence/PostgresHumanAgentRegistry.cs
// Hibrit cache + PostgreSQL human agent kayıt deposu.
// IHumanAgentRegistry implementasyonu — InMemoryHumanAgentRegistry'nin DB-backed karşılığı.
//
// Davranış:
//   - Lazy hydrate: ilk read'de tüm kayıtlar DB'den cache'e çekilir.
//   - Create/Update/Delete: DB + cache senkron güncellenir.
//   - IncrementLoad/DecrementLoad: cache üzerinden per-agent lock + DB UPDATE.

using System.Collections.Concurrent;
using System.Text.Json;
using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Hitl;
using CustomerSupportBot.Domain.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

public sealed class PostgresHumanAgentRegistry : IHumanAgentRegistry
{
    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;
    private readonly ILogger<PostgresHumanAgentRegistry> _logger;
    private readonly ConcurrentDictionary<string, HumanAgent> _agents = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _hydrationLock = new();
    private volatile bool _hydrated;

    public PostgresHumanAgentRegistry(
        IDbContextFactory<CustomerSupportDbContext> dbFactory,
        ILogger<PostgresHumanAgentRegistry> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Routing] HumanAgent hydration başarısız");
            }
            _hydrated = true;
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
        LastAssignedAt = e.LastAssignedAt
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
        LastAssignedAt = a.LastAssignedAt
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
}


// Services/Routing/InMemoryHumanAgentRegistry.cs
// InMemoryHumanAgentRegistry — Process içi human agent kayıtları.
// Restart'ta seed config'den yeniden yüklenir. Production: PostgresHumanAgentRegistry.

using System.Collections.Concurrent;
using CustomerSupportBot.Api.Models;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Services.Routing;

public class InMemoryHumanAgentRegistry : IHumanAgentRegistry
{
    private readonly ConcurrentDictionary<string, HumanAgent> _agents = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryHumanAgentRegistry(IOptions<RoutingOptions> options)
    {
        // Seed
        foreach (var seed in options.Value.SeedAgents ?? new List<HumanAgent>())
        {
            // Defensive copy — seed listesinin instance'ını paylaşma.
            var agent = new HumanAgent
            {
                Id = string.IsNullOrWhiteSpace(seed.Id) ? Guid.NewGuid().ToString("N")[..8] : seed.Id,
                DisplayName = seed.DisplayName,
                Email = seed.Email,
                Skills = NormalizeTags(seed.Skills),
                Languages = NormalizeTags(seed.Languages),
                IsActive = seed.IsActive,
                MaxConcurrentLoad = seed.MaxConcurrentLoad <= 0 ? 5 : seed.MaxConcurrentLoad,
                CurrentLoad = Math.Max(0, seed.CurrentLoad),
                Priority = seed.Priority,
                CreatedAt = seed.CreatedAt == default ? DateTime.UtcNow : seed.CreatedAt
            };
            _agents[agent.Id] = agent;
        }
    }

    public IReadOnlyList<HumanAgent> GetAll() =>
        _agents.Values.OrderByDescending(a => a.IsActive).ThenBy(a => a.DisplayName).ToList();

    public IReadOnlyList<HumanAgent> GetActive() =>
        _agents.Values.Where(a => a.IsActive).ToList();

    public HumanAgent? Get(string id) =>
        string.IsNullOrWhiteSpace(id) ? null
        : _agents.TryGetValue(id, out var a) ? a : null;

    public HumanAgent Create(HumanAgent agent)
    {
        if (string.IsNullOrWhiteSpace(agent.Id))
            agent.Id = Guid.NewGuid().ToString("N")[..8];
        agent.Skills = NormalizeTags(agent.Skills);
        agent.Languages = NormalizeTags(agent.Languages);
        agent.CreatedAt = agent.CreatedAt == default ? DateTime.UtcNow : agent.CreatedAt;
        if (agent.MaxConcurrentLoad <= 0) agent.MaxConcurrentLoad = 5;
        _agents[agent.Id] = agent;
        return agent;
    }

    public HumanAgent? Update(string id, HumanAgentInput input)
    {
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

        return existing;
    }

    public bool Delete(string id) => _agents.TryRemove(id, out _);

    public bool IncrementLoad(string id)
    {
        if (!_agents.TryGetValue(id, out var a)) return false;
        lock (a)
        {
            a.CurrentLoad = Math.Min(a.CurrentLoad + 1, int.MaxValue);
            a.LastAssignedAt = DateTime.UtcNow;
        }
        return true;
    }

    public bool DecrementLoad(string id)
    {
        if (!_agents.TryGetValue(id, out var a)) return false;
        lock (a)
        {
            a.CurrentLoad = Math.Max(a.CurrentLoad - 1, 0);
        }
        return true;
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

// Application/Services/Escalation/AgentPresenceService.cs
// Temsilci çevrimiçi/uzakta durumu: seçim, panel açılışı, kalp atışı, yönetici görünümü.

using System.Globalization;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Routing;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.Escalation;

/// <summary>
/// <see cref="IAgentPresencePort"/> uygulaması. Geçerli durum zaman aşımıyla okuma anında hesaplanır
/// (<see cref="HumanAgent.EffectivePresence"/>); yönlendirme (<see cref="SkillsBasedRouter"/>) aynı
/// kuralı ve aynı <see cref="RoutingOptions.PresenceTimeout"/>'u kullanır.
/// </summary>
public sealed class AgentPresenceService(
    IHumanAgentRegistry agents,
    IOptions<RoutingOptions> options,
    TimeProvider? clock = null) : IAgentPresencePort
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private static readonly StringComparer NameOrder = StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: true);

    private readonly TimeSpan _timeout = options.Value.PresenceTimeout;

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public AgentPresenceInfo? Get(string agentId) =>
        agents.Get(agentId) is { } agent ? ToInfo(agent, Now) : null;

    public AgentPresenceInfo? Set(string agentId, AgentPresence presence) =>
        agents.SetPresence(agentId, presence, Now) ? Get(agentId) : null;

    public AgentPresenceInfo? Connect(string agentId)
    {
        var agent = agents.Get(agentId);
        if (agent is null) return null;
        var ok = agent.Presence == AgentPresence.Offline
            ? agents.SetPresence(agentId, AgentPresence.Online, Now)
            : agents.TouchPresence(agentId, Now);
        return ok ? Get(agentId) : null;
    }

    public AgentPresenceInfo? Heartbeat(string agentId) =>
        agents.TouchPresence(agentId, Now) ? Get(agentId) : null;

    public IReadOnlyList<AgentPresenceInfo> GetAll()
    {
        var now = Now;
        return agents.GetActive()
            .Select(a => ToInfo(a, now))
            .OrderBy(i => i.Presence switch
            {
                AgentPresence.Online => 0,
                AgentPresence.Away => 1,
                _ => 2
            })
            .ThenBy(i => i.DisplayName, NameOrder)
            .ToList();
    }

    private AgentPresenceInfo ToInfo(HumanAgent a, DateTime now) => new(
        a.Id, a.DisplayName, a.EffectivePresence(now, _timeout), a.Presence,
        a.PresenceChangedAt, a.LastSeenAt, a.CurrentLoad, a.MaxConcurrentLoad);
}

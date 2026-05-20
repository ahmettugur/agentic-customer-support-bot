// Application/Services/HumanAgentPortService.cs
// DRIVING PORT IMPL — IHumanAgentPort → IHumanAgentRegistry + IEscalationSink.

using CustomerSupportBot.Application.Ports.Driven.Persistence;
using CustomerSupportBot.Application.Ports.Driving;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services;

public sealed class HumanAgentPortService : IHumanAgentPort
{
    private readonly IHumanAgentRegistry _agents;
    private readonly IEscalationSink _escalations;

    public HumanAgentPortService(IHumanAgentRegistry agents, IEscalationSink escalations)
    {
        _agents = agents;
        _escalations = escalations;
    }

    public async Task<IReadOnlyList<HumanAgent>> GetAllMergedAsync(CancellationToken ct = default)
    {
        var all = _agents.GetAll();
        var linked = await _agents.GetLinkedUsersAsync(ct);
        var registryIds = all.Select(a => a.Id).ToHashSet();
        return all
            .Concat(linked.Where(u => !registryIds.Contains(u.Id)))
            .OrderBy(a => a.DisplayName)
            .ToList();
    }

    public HumanAgent? GetAgent(string id) => _agents.Get(id);

    public HumanAgent CreateAgent(HumanAgent agent) => _agents.Create(agent);

    public HumanAgent? UpdateAgent(string id, HumanAgentInput input) => _agents.Update(id, input);

    public bool DeleteAgent(string id) => _agents.Delete(id);

    public RerouteResult RerouteEscalation(string escalationId, string? agentId, string? reason)
    {
        var esc = _escalations.Get(escalationId);
        if (esc is null)
            return new RerouteResult(null, "Escalation not found.");

        HumanAgent? newAgent = null;
        if (!string.IsNullOrWhiteSpace(agentId))
        {
            newAgent = _agents.Get(agentId);
            if (newAgent is null)
                return new RerouteResult(null, "Agent not found.");
        }

        if (!string.IsNullOrWhiteSpace(esc.SuggestedAgentId))
            _agents.DecrementLoad(esc.SuggestedAgentId);

        esc.SuggestedAgentId = newAgent?.Id;
        esc.SuggestedAgentName = newAgent?.DisplayName;
        esc.RoutingNote = string.IsNullOrWhiteSpace(reason)
            ? $"Manuel atama: {newAgent?.DisplayName ?? "atama kaldırıldı"}."
            : reason;

        if (newAgent is not null)
            _agents.IncrementLoad(newAgent.Id);

        return new RerouteResult(esc, null);
    }
}

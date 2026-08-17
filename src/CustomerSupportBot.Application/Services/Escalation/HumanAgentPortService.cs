// Application/Services/HumanAgentPortService.cs
// DRIVING PORT IMPL — IHumanAgentPort → IHumanAgentRegistry + IEscalationSink.

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Escalation;

public sealed class HumanAgentPortService : IHumanAgentPort, IDisposable
{
    private readonly IHumanAgentRegistry _agents;
    private readonly IEscalationSink _escalations;
    private readonly ILogger<HumanAgentPortService> _logger;
    private readonly EventHandler<EscalationRequest> _loadTrackingHandler;

    public HumanAgentPortService(
        IHumanAgentRegistry agents,
        IEscalationSink escalations,
        ILogger<HumanAgentPortService> logger)
    {
        _agents = agents;
        _escalations = escalations;
        _logger = logger;

        _loadTrackingHandler = (_, esc) =>
        {
            if (esc.Status is EscalationStatus.Resolved or EscalationStatus.Dismissed
                && !string.IsNullOrWhiteSpace(esc.SuggestedAgentId))
            {
                _agents.DecrementLoad(esc.SuggestedAgentId);
                _logger.LogDebug(
                    "RoutingLoadTracker: {AgentId} load decremented (escalation {EscId} {Status})",
                    esc.SuggestedAgentId, esc.Id, esc.Status);
            }
        };
        _escalations.RequestDecided += _loadTrackingHandler;
    }

    public void Dispose()
    {
        _escalations.RequestDecided -= _loadTrackingHandler;
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

    public bool IncrementLoad(string id) => _agents.IncrementLoad(id);

    public bool DecrementLoad(string id) => _agents.DecrementLoad(id);

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

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Inbound;

public sealed record RerouteResult(EscalationRequest? Updated, string? Error);

/// <summary>
/// İnsan temsilci CRUD ve eskalasyon reroute işlemleri için primary (driving) port.
/// </summary>
public interface IHumanAgentPort
{
    Task<IReadOnlyList<HumanAgent>> GetAllMergedAsync(CancellationToken ct = default);
    HumanAgent? GetAgent(string id);
    HumanAgent CreateAgent(HumanAgent agent);
    HumanAgent? UpdateAgent(string id, HumanAgentInput input);
    bool DeleteAgent(string id);
    bool IncrementLoad(string id);
    bool DecrementLoad(string id);
    RerouteResult RerouteEscalation(string escalationId, string? agentId, string? reason);
}

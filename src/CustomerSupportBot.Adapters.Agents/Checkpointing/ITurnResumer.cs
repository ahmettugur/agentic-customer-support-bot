// Adapters.Agents/Checkpointing/ITurnResumer.cs
// Yarım kalan bir turu checkpoint'inden sürdüren koşucu — kurtarma servisinin testlerde sahtelenebilmesi için ayrı arayüz.

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using Microsoft.Agents.AI.Workflows;
using AgentSession = CustomerSupportBot.Domain.Model.AgentSession;

namespace CustomerSupportBot.Adapters.Agents.Checkpointing;

public interface ITurnResumer
{
    /// <summary>Turu <paramref name="from"/> checkpoint'inden bitirir ve nihai yanıt metnini döner.</summary>
    Task<string> ResumeAsync(WorkflowTurnContext turn, AgentSession? session, CheckpointInfo from, CancellationToken ct);
}

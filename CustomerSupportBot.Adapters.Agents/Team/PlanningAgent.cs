// Adapters.Agents/Team/PlanningAgent.cs
// Müşteri talebini analiz eder, yapılandırılmış bir plan (JSON) üretir ve uygun
// specialist ajana yönlendirir. Tool'u yoktur — yalnızca yönlendirme kararı verir.

using CustomerSupportBot.Domain.Model;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Adapters.Agents.Team;

internal sealed class PlanningAgent : SupportAgentBase
{
    public PlanningAgent(IChatClient chatClient, IPromptRepository prompts)
        : base(BuildInner(chatClient, prompts))
    {
    }

    private static ChatClientAgent BuildInner(IChatClient chatClient, IPromptRepository prompts)
        => new(
            chatClient,
            instructions: prompts.Get("agents/planning-agent"),
            name: WellKnown.AgentNames.Planning,
            description: "Müşteri destek görevlerini planlayan ve uygun ajanlara yönlendiren bir ajandır.");

    /// <summary>
    /// BREAKPOINT BURAYA: LLM'e gönderilen tam mesaj listesi — kullanıcı sorgusu,
    /// reasoning hint'i ve ENTITY EXTRACTION hint'i burada görülebilir.
    /// </summary>
    protected override void OnBeforeRun(IReadOnlyList<ChatMessage> messages)
    {
        _ = messages;
    }

    /// <summary>
    /// BREAKPOINT BURAYA: <c>response.Text</c> — üretilen plan JSON'u
    /// (selectedAgent, detectedIntent, intentConfidence, needsClarification…).
    /// Routing kararı bu çıktıdan parse edilir (bkz. PlanRoutingStrategy).
    /// </summary>
    protected override void OnAfterRun(AgentResponse response)
    {
        _ = response.Text;
    }
}

// Adapters.Agents/Team/HumanHandoffAgent.cs
// Kullanıcı açıkça insan/canlı temsilci istediğinde devreye girer ("temsilci bağla",
// "bottan sıkıldım" vb.). Somut bir iş yapmaz; eskalasyon kaydı oluşturur.

using CustomerSupportBot.Application.Services.Tools;
using CustomerSupportBot.Domain.Model;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Adapters.Agents.Team;

internal sealed class HumanHandoffAgent : SupportAgentBase
{
    public HumanHandoffAgent(IChatClient chatClient, IPromptRepository prompts)
        : base(BuildInner(chatClient, prompts))
    {
    }

    private static ChatClientAgent BuildInner(IChatClient chatClient, IPromptRepository prompts)
        => new(
            chatClient,
            instructions: prompts.Get("agents/human-handoff-agent"),
            name: WellKnown.AgentNames.HumanHandoff,
            description: "Kullanıcının açıkça insan temsilcisiyle görüşme talebini karşılar.",
            tools: [
                AIFunctionFactory.Create(
                    CustomerSupportToolsService.HumanHandoffTool,
                    new AIFunctionFactoryOptions { Name = WellKnown.ToolNames.HumanHandoff })
            ]);

    /// <summary>
    /// BREAKPOINT BURAYA: LLM'e gönderilen tam mesaj listesi.
    /// </summary>
    protected override void OnBeforeRun(IReadOnlyList<ChatMessage> messages)
    {
        _ = messages;
    }

    /// <summary>
    /// BREAKPOINT BURAYA: <c>toolCalls</c> — human_handoff_tool hangi gerekçeyle
    /// çağrıldı, <c>response.Text</c> — kullanıcıya iletilecek yönlendirme metni.
    /// </summary>
    protected override void OnAfterRun(AgentResponse response)
    {
        var toolCalls = ToolCalls(response);
        var toolResults = ToolResults(response);

        _ = toolCalls;
        _ = toolResults;
        _ = response.Text;
    }
}

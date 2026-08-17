// Adapters.Agents/Team/ComplaintAgent.cs
// Müşteri şikayetlerini kaydeder. Tek tool'u yan etkilidir ve HITL approval
// gate'inden geçer (admin onayı beklenir).

using CustomerSupportBot.Domain.Model;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Adapters.Agents.Team;

internal sealed class ComplaintAgent : SupportAgentBase
{
    public ComplaintAgent(
        IChatClient chatClient,
        IPromptRepository prompts,
        ApprovalGateService approvalGate)
        : base(BuildInner(chatClient, prompts, approvalGate))
    {
    }

    private static ChatClientAgent BuildInner(
        IChatClient chatClient,
        IPromptRepository prompts,
        ApprovalGateService approvalGate)
        => new(
            chatClient,
            new ChatClientAgentOptions
            {
                Name = WellKnown.AgentNames.Complaint,
                Description = "Müşteri şikayetlerini işler.",
                ChatOptions = new ChatOptions
                {
                    Instructions = prompts.Get("agents/complaint-agent"),
                    Tools = [approvalGate.BuildComplaintRegistrationTool()],
                    ResponseFormat = ChatResponseFormat.ForJsonSchema<SpecialistReasoningSchema>(
                        SpecialistReasoningSchemaOptions.CamelCase)
                }
            });

    /// <summary>
    /// BREAKPOINT BURAYA: LLM'e gönderilen tam mesaj listesi.
    /// </summary>
    protected override void OnBeforeRun(IReadOnlyList<ChatMessage> messages)
    {
        _ = messages;
    }

    /// <summary>
    /// BREAKPOINT BURAYA: <c>toolCalls</c> — complaint_registration_tool hangi
    /// argümanlarla çağrıldı (orderId, complaintText, customerId), <c>toolResults</c> —
    /// onay reddedildiyse ValidationError burada görülür.
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

// Adapters.Agents/Team/OrderAgent.cs
// Sipariş oluşturma, sorgulama, iptal ve iade işlemleri.
// Tüm tool'lar (salt-okunur olanlar dahil) ApprovalGateService üzerinden kurulur —
// customerId login'den (JWT/ApprovalContext) otomatik alınır, LLM'e hiç parametre olarak
// gösterilmez. Yan etkili olanlar (placement / cancel / return) ayrıca HITL approval
// gate'inden geçer.

using CustomerSupportBot.Domain.Model;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Adapters.Agents.Team;

internal sealed class OrderAgent : SupportAgentBase
{
    public OrderAgent(
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
                Name = WellKnown.AgentNames.Order,
                Description = "Sipariş oluşturma, sorgulama, iptal ve iade işlemlerini yürütür.",
                ChatOptions = new ChatOptions
                {
                    Instructions = prompts.Get("agents/order-agent"),
                    Tools = [
                        approvalGate.BuildOrderPlacementTool(),
                        approvalGate.BuildOrderStatusTool(),
                        approvalGate.BuildGetLastOrderTool(),
                        approvalGate.BuildGetAllOrdersTool(),
                        approvalGate.BuildOrderCancelTool(),
                        approvalGate.BuildReturnRequestTool()
                    ],
                    ResponseFormat = ChatResponseFormat.ForJsonSchema<SpecialistReasoningSchema>(
                        SpecialistReasoningSchemaOptions.CamelCase)
                }
            });

    /// <summary>
    /// BREAKPOINT BURAYA: LLM'e gönderilen tam mesaj listesi — kullanıcı sorgusu,
    /// sistem hint'leri (ör. ENTITY EXTRACTION → order_id) ve o ana kadarki grup sohbeti.
    /// </summary>
    protected override void OnBeforeRun(IReadOnlyList<ChatMessage> messages)
    {
        _ = messages;
    }

    /// <summary>
    /// BREAKPOINT BURAYA: <c>toolCalls</c> — hangi sipariş tool'u hangi argümanlarla
    /// çağrıldı (ör. order_status_tool { orderId = "1043" }), <c>toolResults</c> — sonucu
    /// (ToolResult; not-found burada görülür), <c>response.Text</c> — ajanın metni.
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

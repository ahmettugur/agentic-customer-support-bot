// Adapters.Agents/Team/OrderAgent.cs
// Sipariş oluşturma, sorgulama, iptal ve iade işlemleri.
// Salt-okunur tool'lar (order_status / get_last_order / get_all_orders) doğrudan;
// yan etkili olanlar (placement / cancel / return) HITL approval gate'inden geçer.

using CustomerSupportBot.Domain.Model;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Adapters.Agents.Team;

internal sealed class OrderAgent : SupportAgentBase
{
    public OrderAgent(
        IChatClient chatClient,
        IPromptRepository prompts,
        ApprovalGateService approvalGate,
        ICustomerSupportToolsService tools)
        : base(BuildInner(chatClient, prompts, approvalGate, tools))
    {
    }

    private static ChatClientAgent BuildInner(
        IChatClient chatClient,
        IPromptRepository prompts,
        ApprovalGateService approvalGate,
        ICustomerSupportToolsService tools)
        => new(
            chatClient,
            instructions: prompts.Get("agents/order-agent"),
            name: WellKnown.AgentNames.Order,
            description: "Sipariş oluşturma, sorgulama, iptal ve iade işlemlerini yürütür.",
            tools: [
                approvalGate.BuildOrderPlacementTool(),
                AIFunctionFactory.Create(tools.OrderStatusTool,  new AIFunctionFactoryOptions { Name = WellKnown.ToolNames.OrderStatus }),
                AIFunctionFactory.Create(tools.GetLastOrderTool, new AIFunctionFactoryOptions { Name = WellKnown.ToolNames.GetLastOrder }),
                AIFunctionFactory.Create(tools.GetAllOrdersTool, new AIFunctionFactoryOptions { Name = WellKnown.ToolNames.GetAllOrders }),
                approvalGate.BuildOrderCancelTool(),
                approvalGate.BuildReturnRequestTool()
            ]);

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

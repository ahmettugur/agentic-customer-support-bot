// Adapters.Agents/AgentTeamFactory.cs
// 6 specialist ChatClientAgent'ı kurar ve bunlardan bir GroupChat Workflow üretir.
//
// Mimari:
// 1. PlanningAgent       → Yönlendirme (araç yok)
// 2. ProductAgent        → product_inquiry_tool + product_list_tool
// 3. OrderAgent          → order_placement_tool (HITL) + order_status_tool + get_last_order_tool + get_all_orders_tool
// 4. ComplaintAgent      → complaint_registration_tool (HITL approval gate)
// 5. HumanHandoffAgent   → human_handoff_tool
// 6. ResponseAgent       → Son yanıt biçimlendirme, "TERMINATE" ile sonlandırma

using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Application.Services.Tools;
using CustomerSupportBot.Domain.Model;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Agents;

/// <summary>
/// Specialist ajanları kurar ve her workflow koşusu için taze bir GroupChat
/// <see cref="Workflow"/> üretir. Ajan örnekleri (dolayısıyla tool bağlamları) süreç
/// ömrü boyunca sabittir; her koşu yalnızca yeni bir <see cref="CustomerSupportChatManager"/>
/// ve graph bağlantıları kurar (bkz. <see cref="CreateWorkflow"/> — <c>_handoffCounts</c> gibi
/// tur-başına izole olması gereken durum bu şekilde garanti edilir).
/// </summary>
internal sealed class AgentTeamFactory
{
    public AIAgent PlanningAgent { get; }
    public AIAgent ProductAgent { get; }
    public AIAgent OrderAgent { get; }
    public AIAgent ComplaintAgent { get; }
    public AIAgent HumanHandoffAgent { get; }
    public AIAgent ResponseAgent { get; }

    private readonly WorkflowGuardOptions _guards;
    private readonly ILoggerFactory _loggerFactory;

    public AgentTeamFactory(
        IChatClient chatClient,
        IPromptRepository prompts,
        ApprovalGateService approvalGate,
        ICustomerSupportToolsService tools,
        WorkflowGuardOptions guards,
        ILoggerFactory loggerFactory)
    {
        _guards = guards;
        _loggerFactory = loggerFactory;

        var sourceName = TelemetryConstants.ActivitySourceName;

        PlanningAgent = WrapWithTelemetry(new ChatClientAgent(
            chatClient,
            instructions: prompts.Get("agents/planning-agent"),
            name: WellKnown.AgentNames.Planning,
            description: "Müşteri destek görevlerini planlayan ve uygun ajanlara yönlendiren bir ajandır."), sourceName);

        ProductAgent = WrapWithTelemetry(new ChatClientAgent(
            chatClient,
            instructions: prompts.Get("agents/product-agent"),
            name: WellKnown.AgentNames.Product,
            description: "Ürün sorgularını yanıtlar.",
            tools: [
                AIFunctionFactory.Create(tools.ProductInquiryTool, new AIFunctionFactoryOptions { Name = WellKnown.ToolNames.ProductInquiry }),
                AIFunctionFactory.Create(tools.ProductListTool,    new AIFunctionFactoryOptions { Name = WellKnown.ToolNames.ProductList })
            ]), sourceName);

        OrderAgent = WrapWithTelemetry(new ChatClientAgent(
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
            ]), sourceName);

        ComplaintAgent = WrapWithTelemetry(new ChatClientAgent(
            chatClient,
            instructions: prompts.Get("agents/complaint-agent"),
            name: WellKnown.AgentNames.Complaint,
            description: "Müşteri şikayetlerini işler.",
            tools: [approvalGate.BuildComplaintRegistrationTool()]), sourceName);

        HumanHandoffAgent = WrapWithTelemetry(new ChatClientAgent(
            chatClient,
            instructions: prompts.Get("agents/human-handoff-agent"),
            name: WellKnown.AgentNames.HumanHandoff,
            description: "Kullanıcının açıkça insan temsilcisiyle görüşme talebini karşılar.",
            tools: [AIFunctionFactory.Create(CustomerSupportToolsService.HumanHandoffTool, new AIFunctionFactoryOptions { Name = WellKnown.ToolNames.HumanHandoff })]), sourceName);

        ResponseAgent = WrapWithTelemetry(new ChatClientAgent(
            chatClient,
            instructions: prompts.Get("agents/response-agent"),
            name: WellKnown.AgentNames.Response,
            description: "Yanıtları biçimlendirir ve kullanıcıya iletir."), sourceName);
    }

    private static AIAgent WrapWithTelemetry(ChatClientAgent agent, string sourceName)
        => agent.AsBuilder().UseOpenTelemetry(sourceName).Build();

    public Workflow CreateWorkflow()
    {
        return AgentWorkflowBuilder
            .CreateGroupChatBuilderWith(agents =>
            {
                return new CustomerSupportChatManager(
                    agents,
                    _guards,
                    _loggerFactory.CreateLogger<CustomerSupportChatManager>())
                {
                    MaximumIterationCount = _guards.MaxIterations
                };
            })
            .AddParticipants(
                PlanningAgent,
                ProductAgent,
                OrderAgent,
                ComplaintAgent,
                HumanHandoffAgent,
                ResponseAgent)
            .Build();
    }
}

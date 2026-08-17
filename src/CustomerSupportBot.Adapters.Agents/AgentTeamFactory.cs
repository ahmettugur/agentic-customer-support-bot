// Adapters.Agents/AgentTeamFactory.cs
// 6 ajanı örnekler ve bunlardan her koşu için taze bir GroupChat Workflow üretir.
// Her ajanın kendi prompt'u, adı, açıklaması ve tool listesi KENDİ sınıfındadır —
// bkz. Team/ klasörü (PlanningAgent, ProductAgent, OrderAgent, ComplaintAgent,
// HumanHandoffAgent, ResponseAgent — hepsi SupportAgentBase'den türer).

using CustomerSupportBot.Adapters.Agents.Team;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Domain.Model;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Agents;

/// <summary>
/// Ajanları örnekler ve her workflow koşusu için taze bir GroupChat
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

        PlanningAgent     = WrapWithTelemetry(new PlanningAgent(chatClient, prompts), sourceName);
        ProductAgent      = WrapWithTelemetry(new ProductAgent(chatClient, prompts, tools), sourceName);
        OrderAgent        = WrapWithTelemetry(new OrderAgent(chatClient, prompts, approvalGate), sourceName);
        ComplaintAgent    = WrapWithTelemetry(new ComplaintAgent(chatClient, prompts, approvalGate), sourceName);
        HumanHandoffAgent = WrapWithTelemetry(new HumanHandoffAgent(chatClient, prompts), sourceName);
        ResponseAgent     = WrapWithTelemetry(new ResponseAgent(chatClient, prompts), sourceName);
    }

    private static AIAgent WrapWithTelemetry(AIAgent agent, string sourceName)
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

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
    private readonly IApprovalContextAccessor _approvalContext;

    public AgentTeamFactory(
        IChatClient chatClient,
        IPromptRepository prompts,
        ApprovalGateService approvalGate,
        ICustomerSupportToolsService tools,
        WorkflowGuardOptions guards,
        ILoggerFactory loggerFactory,
        IApprovalContextAccessor approvalContext,
        ITurnUsageTracker? usage = null)
    {
        _guards = guards;
        _loggerFactory = loggerFactory;
        _approvalContext = approvalContext;

        var sourceName = TelemetryConstants.ActivitySourceName;

        // Tur sonu token özeti için her ajan kendi etiketiyle sarılır (bkz. UsageLabelingChatClient).
        IChatClient For(string label) => usage is null ? chatClient : new UsageLabelingChatClient(chatClient, label, usage);

        PlanningAgent     = WrapWithTelemetry(new PlanningAgent(For(nameof(PlanningAgent)), prompts), sourceName);
        ProductAgent      = WrapWithTelemetry(new ProductAgent(For(nameof(ProductAgent)), prompts, tools), sourceName);
        OrderAgent        = WrapWithTelemetry(new OrderAgent(For(nameof(OrderAgent)), prompts, approvalGate), sourceName);
        ComplaintAgent    = WrapWithTelemetry(new ComplaintAgent(For(nameof(ComplaintAgent)), prompts, approvalGate), sourceName);
        HumanHandoffAgent = WrapWithTelemetry(new HumanHandoffAgent(For(nameof(HumanHandoffAgent)), prompts), sourceName);
        ResponseAgent     = WrapWithTelemetry(new ResponseAgent(For(nameof(ResponseAgent)), prompts), sourceName);
    }

    private AIAgent WrapWithTelemetry(AIAgent agent, string sourceName)
        => agent.AsBuilder().Use(InvokeToolAsync).UseOpenTelemetry(sourceName).Build();

    private async ValueTask<object?> InvokeToolAsync(AIAgent agent, FunctionInvocationContext invocation,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next, CancellationToken ct)
    {
        var context = _approvalContext.Context;
        using var scope = _approvalContext.SetScope(context?.SessionId, context?.TraceId, context?.UserQuery, context?.CustomerId);
        _approvalContext.SetCurrentAgent(agent.Name);
        return await next(invocation, ct).ConfigureAwait(false);
    }

    /// <param name="firstAgentName">Doluysa ilk konuşan ajan budur ve PlanningAgent atlanır (bkz. <c>IntentRouter</c>); boşsa eski yol.</param>
    public Workflow CreateWorkflow(string? constrainedSpecialistName = null, string? firstAgentName = null)
    {
        return AgentWorkflowBuilder
            .CreateGroupChatBuilderWith(agents =>
            {
                return new CustomerSupportChatManager(
                    agents,
                    _guards,
                    _loggerFactory.CreateLogger<CustomerSupportChatManager>(),
                    constrainedSpecialistName,
                    firstAgentName)
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

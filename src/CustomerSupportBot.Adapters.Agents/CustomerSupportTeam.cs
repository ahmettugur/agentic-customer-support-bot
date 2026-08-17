// Adapters.Agents/CustomerSupportTeam.cs
// IAgentTeamPort implementasyonu — kompozisyon kökü.
// Gerçek iş AgentTeamFactory (ajan/workflow kurulumu), WorkflowRunner (tek tur koşusu +
// trace toplama + streaming), DecomposedRunner (compound query orkestrasyonu) ve
// TurnFinalizer (tur sonu yan etkileri) arasında bölünmüştür — bu sınıf yalnızca
// compound/single query ayrımını yapıp doğru koşucuya yönlendirir.

using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Services.Providers;
using CustomerSupportBot.Application.Services.Reasoning;
using CustomerSupportBot.Domain.Model;
using AgentSession = CustomerSupportBot.Domain.Model.AgentSession;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.Agents;

/// <summary>
/// Müşteri destek ajan takımını yönetir.
/// </summary>
public class CustomerSupportTeam : IAgentTeamPort
{
    private readonly WorkflowRunner _runner;
    private readonly DecomposedRunner _decomposed;

    public CustomerSupportTeam(
        IChatClient chatClient,
        IContextPipeline contextPipeline,
        IOptions<WorkflowGuardOptions> guardOptions,
        IOptions<ParallelExecutionOptions> parallelOptions,
        IReasoningTraceStore traceStore,
        IPromptRepository prompts,
        ApprovalGateService approvalGate,
        ICustomerSupportToolsService tools,
        IUiHintEmitter uiHint,
        IApprovalContextAccessor approvalContext,
        ILoggerFactory loggerFactory,
        CustomerIdentityHintBuilder identityHint,
        ISemanticMemoryWriter? semanticMemory = null,
        ICustomerProfileService? profileService = null)
    {
        var guards = guardOptions.Value;

        var factory = new AgentTeamFactory(chatClient, prompts, approvalGate, tools, guards, loggerFactory);
        var finalizer = new TurnFinalizer(traceStore, approvalGate, loggerFactory, semanticMemory, profileService);
        var traceProcessor = new WorkflowTraceEventProcessor(traceStore, approvalContext);
        var messageBuilder = new WorkflowMessageBuilder(contextPipeline, prompts, chatClient, loggerFactory, identityHint);

        _runner = new WorkflowRunner(
            factory, finalizer, guards, traceStore,
            approvalGate, uiHint, loggerFactory, traceProcessor, messageBuilder);

        _decomposed = new DecomposedRunner(_runner, parallelOptions.Value, uiHint, approvalContext);
    }

    public Task<string> RunAsync(
        string query,
        List<ConversationMessage>? conversationHistory = null,
        AgentSession? session = null,
        ReasoningResult? reasoning = null,
        CancellationToken ct = default)
    {
        return SubTaskOrchestrator.IsCompoundQuery(reasoning)
            ? _decomposed.RunDecomposedAsync(query, conversationHistory, session, reasoning!, ct)
            : _runner.RunAsync(query, conversationHistory, session, reasoning, ct);
    }

    public IAsyncEnumerable<StreamEvent> RunStreamingAsync(
        string query,
        List<ConversationMessage>? conversationHistory = null,
        AgentSession? session = null,
        ReasoningResult? reasoning = null,
        CancellationToken ct = default)
    {
        return SubTaskOrchestrator.IsCompoundQuery(reasoning)
            ? _decomposed.RunDecomposedStreamingAsync(query, conversationHistory, session, reasoning!, ct)
            : _runner.RunStreamingAsync(query, conversationHistory, session, reasoning, ct);
    }

    public string GetWorkflowDiagram() => _runner.GetWorkflowDiagram();
}

// Adapters.Agents/CustomerSupportTeam.cs
// Müşteri destek ajan takımını yönetir.
// Ajan oluşturma + workflow execution + streaming delegasyonu.
//
// Mimari:
// 1. PlanningAgent       → Yönlendirme (araç yok)
// 2. ProductAgent         → product_inquiry_tool + product_list_tool
// 3. OrderAgent          → order_placement_tool (HITL) + order_status_tool + get_last_order_tool + get_all_orders_tool
// 4. ComplaintAgent      → complaint_registration_tool (HITL approval gate)
// 5. HumanHandoffAgent   → human_handoff_tool
// 6. ResponseAgent       → Son yanıt biçimlendirme, "TERMINATE" ile sonlandırma

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using CustomerSupportBot.Application.Ports.Driven;
using CustomerSupportBot.Application.Ports.Driven.Observability;
using CustomerSupportBot.Application.Ports.Driving;
using CustomerSupportBot.Application.Services;
using CustomerSupportBot.Application.Services.Memory;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;
using AgentSession = CustomerSupportBot.Domain.Model.AgentSession;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.Agents;

/// <summary>
/// Müşteri destek ajan takımını yönetir.
/// Ajanları oluşturur, workflow'u başlatır ve sonuçları işler.
/// </summary>
public class CustomerSupportTeam : IAgentTeamPort
{
    private readonly AIAgent _planningAgent;
    private readonly AIAgent _productAgent;
    private readonly AIAgent _orderAgent;
    private readonly AIAgent _complaintAgent;
    private readonly AIAgent _humanHandoffAgent;
    private readonly AIAgent _responseAgent;
    private readonly IContextPipeline _contextPipeline;
    private readonly IChatClient _chatClient;
    private readonly WorkflowGuardOptions _guards;
    private readonly IReasoningTraceStore _traceStore;
    private readonly IPromptRepository _prompts;
    private readonly ApprovalGateService _approvalGate;
    private readonly ICustomerSupportToolsService _tools;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ISemanticMemoryWriter? _semanticMemory;
    private readonly ICustomerProfileService? _profileService;
    private readonly ParallelExecutionOptions _parallelOptions;

    public CustomerSupportTeam(
        IChatClient chatClient,
        IContextPipeline contextPipeline,
        IOptions<WorkflowGuardOptions> guardOptions,
        IOptions<ParallelExecutionOptions> parallelOptions,
        IReasoningTraceStore traceStore,
        IPromptRepository prompts,
        ApprovalGateService approvalGate,
        ICustomerSupportToolsService tools,
        ILoggerFactory loggerFactory,
        ISemanticMemoryWriter? semanticMemory = null,
        ICustomerProfileService? profileService = null)
    {
        _contextPipeline = contextPipeline;
        _chatClient = chatClient;
        _traceStore = traceStore;
        _prompts = prompts;
        _approvalGate = approvalGate;
        _tools = tools;
        _loggerFactory = loggerFactory;
        _semanticMemory = semanticMemory;
        _profileService = profileService;

        _guards = guardOptions.Value;
        _parallelOptions = parallelOptions.Value;

        var sourceName = TelemetryConstants.ActivitySourceName;

        _planningAgent = WrapWithTelemetry(new ChatClientAgent(
            chatClient,
            instructions: _prompts.Get("agents/planning-agent"),
            name: WellKnown.AgentNames.Planning,
            description: "Müşteri destek görevlerini planlayan ve uygun ajanlara yönlendiren bir ajandır."), sourceName);

        _productAgent = WrapWithTelemetry(new ChatClientAgent(
            chatClient,
            instructions: _prompts.Get("agents/product-agent"),
            name: WellKnown.AgentNames.Product,
            description: "Ürün sorgularını yanıtlar.",
            tools: [
                AIFunctionFactory.Create(_tools.ProductInquiryTool, new AIFunctionFactoryOptions { Name = WellKnown.ToolNames.ProductInquiry }),
                AIFunctionFactory.Create(_tools.ProductListTool,    new AIFunctionFactoryOptions { Name = WellKnown.ToolNames.ProductList })
            ]), sourceName);

        _orderAgent = WrapWithTelemetry(new ChatClientAgent(
            chatClient,
            instructions: _prompts.Get("agents/order-agent"),
            name: WellKnown.AgentNames.Order,
            description: "Sipariş oluşturma, sorgulama, iptal ve iade işlemlerini yürütür.",
            tools: [
                _approvalGate.BuildOrderPlacementTool(),
                AIFunctionFactory.Create(_tools.OrderStatusTool,  new AIFunctionFactoryOptions { Name = WellKnown.ToolNames.OrderStatus }),
                AIFunctionFactory.Create(_tools.GetLastOrderTool, new AIFunctionFactoryOptions { Name = WellKnown.ToolNames.GetLastOrder }),
                AIFunctionFactory.Create(_tools.GetAllOrdersTool, new AIFunctionFactoryOptions { Name = WellKnown.ToolNames.GetAllOrders }),
                _approvalGate.BuildOrderCancelTool(),
                _approvalGate.BuildReturnRequestTool()
            ]), sourceName);

        _complaintAgent = WrapWithTelemetry(new ChatClientAgent(
            chatClient,
            instructions: _prompts.Get("agents/complaint-agent"),
            name: WellKnown.AgentNames.Complaint,
            description: "Müşteri şikayetlerini işler.",
            tools: [_approvalGate.BuildComplaintRegistrationTool()]), sourceName);

        _humanHandoffAgent = WrapWithTelemetry(new ChatClientAgent(
            chatClient,
            instructions: _prompts.Get("agents/human-handoff-agent"),
            name: WellKnown.AgentNames.HumanHandoff,
            description: "Kullanıcının açıkça insan temsilcisiyle görüşme talebini karşılar.",
            tools: [AIFunctionFactory.Create(CustomerSupportToolsService.HumanHandoffTool, new AIFunctionFactoryOptions { Name = WellKnown.ToolNames.HumanHandoff })]), sourceName);

        _responseAgent = WrapWithTelemetry(new ChatClientAgent(
            chatClient,
            instructions: _prompts.Get("agents/response-agent"),
            name: WellKnown.AgentNames.Response,
            description: "Yanıtları biçimlendirir ve kullanıcıya iletir."), sourceName);
    }

    private static AIAgent WrapWithTelemetry(ChatClientAgent agent, string sourceName)
        => agent.AsBuilder().UseOpenTelemetry(sourceName).Build();

    private Workflow CreateWorkflow()
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
                _planningAgent,
                _productAgent,
                _orderAgent,
                _complaintAgent,
                _humanHandoffAgent,
                _responseAgent)
            .Build();
    }

    public async Task<string> RunAsync(
        string query,
        List<ConversationMessage>? conversationHistory = null,
        AgentSession? session = null,
        ReasoningResult? reasoning = null)
    {
        if (SubTaskOrchestrator.IsCompoundQuery(reasoning))
        {
            return await RunDecomposedAsync(query, conversationHistory, session, reasoning!);
        }

        var messages = await BuildWorkflowMessagesAsync(query, conversationHistory, session, reasoning);

        var workflow = CreateWorkflow();
        await using var run = await InProcessExecution.RunStreamingAsync(workflow, messages);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        string result = "";

        await foreach (var evt in run.WatchStreamAsync())
        {
            switch (evt)
            {
                case WorkflowOutputEvent output:
                    result = WorkflowResponseExtractor.ExtractResultFromOutput(output);
                    break;
                case WorkflowErrorEvent errorEvt:
                    throw ExceptionTranslator.Translate(
                        errorEvt.Exception ?? new InvalidOperationException("Workflow hatası"),
                        "RunAsync workflow hatası.");
            }
        }

        result = WorkflowResponseExtractor.RemoveTerminationMarkers(result);
        result = WorkflowResponseExtractor.RemoveTechnicalJsonBlocks(result);

        if (WorkflowResponseExtractor.ContainsAgentRoutingMessage(result))
        {
            result = await RewriteRoutingMessageAsync(result, query);
        }

        return result;
    }

    public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(
        string query,
        List<ConversationMessage>? conversationHistory = null,
        AgentSession? session = null,
        ReasoningResult? reasoning = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (SubTaskOrchestrator.IsCompoundQuery(reasoning))
        {
            await foreach (var evt in RunDecomposedStreamingAsync(
                query, conversationHistory, session, reasoning!, ct))
            {
                yield return evt;
            }
            yield break;
        }

        var messages = await BuildWorkflowMessagesAsync(query, conversationHistory, session, reasoning);

        using var timeoutCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(_guards.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var effectiveCt = linkedCts.Token;

        var trace = _traceStore.StartTrace(session?.SessionId ?? "anonymous", query);
        if (reasoning != null)
        {
            trace.Reasoning = reasoning;
            _traceStore.Update(trace);
        }
        var activeVisits = new Dictionary<string, AgentVisit>();

        var workflow = CreateWorkflow();
        await using var run = await InProcessExecution.RunStreamingAsync(workflow, messages);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        string result = "";
        string? lastAgentSignature = null;
        string? workflowError = null;
        int iterationCount = 0;

        await foreach (var (evt, evtError) in EnumerateWorkflowEventsSafely(run, effectiveCt))
        {
            if (evtError != null)
            {
                workflowError = evtError;
                break;
            }
            if (evt == null) continue;

            switch (evt)
            {
                case ExecutorInvokedEvent invoked:
                {
                    var executorId = invoked.ExecutorId ?? "unknown";
                    iterationCount++;
                    if (WorkflowResponseExtractor.IsInternalWorkflowExecutor(executorId)) break;

                    var sig = $"{executorId}:running";
                    if (sig == lastAgentSignature) break;
                    lastAgentSignature = sig;

                    // Visit'i sadece activeVisits'e kaydet; trace listesine CompletedEvent'te
                    // ekleyeceğiz — bu şekilde pasif geçiş turları (0ms) kaydedilmez.
                    activeVisits[executorId] = new AgentVisit
                    {
                        AgentName = executorId,
                        StartedAt = DateTime.UtcNow
                    };

                    yield return new StreamEvent(StreamEventTypes.Agent,
                        new { name = executorId, status = "running" });
                    break;
                }

                case ExecutorCompletedEvent completed:
                {
                    var completedId = completed.ExecutorId ?? "unknown";
                    if (WorkflowResponseExtractor.IsInternalWorkflowExecutor(completedId)) break;

                    var sig = $"{completedId}:done";
                    if (sig == lastAgentSignature) break;
                    lastAgentSignature = sig;

                    if (activeVisits.TryGetValue(completedId, out var visit))
                    {
                        visit.CompletedAt = DateTime.UtcNow;
                        activeVisits.Remove(completedId);

                        // Sadece anlamlı süre olan ziyaretleri kaydet (> 0ms).
                        // 0ms = framework pasif geçiş turu, gerçek iş yok.
                        if (visit.DurationMs is null or > 0)
                        {
                            trace.AgentVisits.Add(visit);
                            _traceStore.Update(trace);
                        }
                    }

                    yield return new StreamEvent(StreamEventTypes.Agent,
                        new { name = completedId, status = "done" });
                    break;
                }

                case WorkflowOutputEvent output:
                    result = WorkflowResponseExtractor.ExtractResultFromOutput(output);
                    var planning = WorkflowResponseExtractor.ExtractPlanningFromOutput(output);
                    if (planning != null) trace.Planning = planning;
                    var specialistReasonings = WorkflowResponseExtractor.ExtractSpecialistReasoningsFromOutput(output);
                    if (specialistReasonings.Count > 0) trace.SpecialistReasonings.AddRange(specialistReasonings);
                    if (planning != null || specialistReasonings.Count > 0)
                        _traceStore.Update(trace);
                    break;

                case WorkflowErrorEvent errorEvt:
                    workflowError = errorEvt.Exception?.Message ?? "workflow error";
                    break;
            }

            if (workflowError != null) break;
        }

        trace.IterationCount = iterationCount;

        if (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _traceStore.Complete(trace.TraceId,
                terminationReason: WellKnown.Termination.ReasonTimeout,
                error: $"Workflow {_guards.TimeoutSeconds}s timeout'a takıldı");
            yield return new StreamEvent(StreamEventTypes.Error,
                new { message = $"İşlem {_guards.TimeoutSeconds} saniyede tamamlanamadı." });
            yield break;
        }

        if (workflowError != null)
        {
            _traceStore.Complete(trace.TraceId, terminationReason: "error", error: workflowError);
            yield return new StreamEvent(StreamEventTypes.Error,
                new { message = workflowError });
            yield break;
        }

        var terminationReason = WorkflowResponseExtractor.ParseTerminationReasonFromResult(result) ?? WellKnown.Termination.ReasonCompleted;
        result = WorkflowResponseExtractor.RemoveTerminationMarkers(result);
        result = WorkflowResponseExtractor.RemoveTechnicalJsonBlocks(result);

        if (WorkflowResponseExtractor.ContainsAgentRoutingMessage(result))
            result = await RewriteRoutingMessageAsync(result, query);

        _approvalGate.ProcessPendingEscalations(trace, query, result);

        PopulateAgentVisitOutputs(trace, result);

        WriteEpisodicMemorySafe(trace, query, result);

        await UpdateCustomerProfileSafeAsync(session, trace, query, result);

        _traceStore.Complete(trace.TraceId,
            terminationReason: terminationReason,
            finalResponse: result);

        yield return new StreamEvent(StreamEventTypes.ResponseStart,
            new { terminationReason });

        await foreach (var chunk in WorkflowResponseExtractor.StreamTextInChunksAsync(result, effectiveCt))
        {
            yield return new StreamEvent(StreamEventTypes.ResponseDelta, new { text = chunk });
        }

        yield return new StreamEvent(StreamEventTypes.ResponseComplete,
            new { text = result, terminationReason });
    }

    private static void PopulateAgentVisitOutputs(ReasoningTrace trace, string finalResult)
    {
        const int MaxLen = 1500;
        static string Truncate(string s) => s.Length <= MaxLen ? s : s[..MaxLen] + "…";

        foreach (var visit in trace.AgentVisits)
        {
            if (!string.IsNullOrWhiteSpace(visit.Output)) continue;

            var name = visit.AgentName ?? "";
            var baseName = name.Split('_', 2)[0];

            string? output = null;

            if (baseName.StartsWith("Planning", StringComparison.OrdinalIgnoreCase) && trace.Planning != null)
            {
                output = JsonSerializer.Serialize(trace.Planning, _prettyJson);
            }
            else if (baseName.StartsWith("Response", StringComparison.OrdinalIgnoreCase))
            {
                output = finalResult;
            }
            else if (trace.SpecialistReasonings.Count > 0)
            {
                var matching = trace.SpecialistReasonings
                    .Where(s => string.Equals(s.AgentName, baseName, StringComparison.OrdinalIgnoreCase)
                             || s.AgentName.StartsWith(baseName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (matching.Count > 0)
                {
                    output = JsonSerializer.Serialize(matching.Count == 1 ? matching[0] : (object)matching, _prettyJson);
                }
            }

            if (!string.IsNullOrWhiteSpace(output))
                visit.Output = Truncate(output);
        }
    }

    private static readonly JsonSerializerOptions _prettyJson = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private void WriteEpisodicMemorySafe(ReasoningTrace trace, string query, string response)
    {
        if (_semanticMemory is null || !_semanticMemory.Enabled) return;
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(response)) return;

        var sessionId = trace.SessionId;
        var traceId = trace.TraceId;
        var intent = trace.Reasoning?.Intent ?? trace.Planning?.DetectedIntent;
        var memory = _semanticMemory;
        var logger = _loggerFactory.CreateLogger<CustomerSupportTeam>();

        _ = Task.Run(async () =>
        {
            try
            {
                await memory.WriteEpisodeAsync(sessionId, traceId, query, response, intent, rating: null);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Episodic memory yazımı başarısız oldu (traceId={TraceId})", traceId);
            }
        });
    }

    private async Task UpdateCustomerProfileSafeAsync(AgentSession? session, ReasoningTrace trace, string query, string response)
    {
        if (_profileService is null) return;
        var customerId = session?.State.CustomerId;
        if (string.IsNullOrWhiteSpace(customerId)) return;

        var intent = trace.Reasoning?.Intent ?? trace.Planning?.DetectedIntent;
        var logger = _loggerFactory.CreateLogger<CustomerSupportTeam>();

        try
        {
            await _profileService.RecordInteractionAsync(
                customerId: customerId,
                userQuery: query,
                botResponse: response,
                intent: intent,
                rating: null,
                isNewSession: false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Customer profile güncellemesi başarısız (customerId={Id})", customerId);
        }
    }

    private async Task<List<ChatMessage>> BuildWorkflowMessagesAsync(
        string query,
        List<ConversationMessage>? conversationHistory,
        AgentSession? session,
        ReasoningResult? reasoning)
    {
        var messages = new List<ChatMessage>();

        if (session != null)
        {
            var context = await _contextPipeline.BuildContextAsync(session);
            if (!string.IsNullOrWhiteSpace(context))
            {
                messages.Add(new ChatMessage(ChatRole.System,
                    $"Aşağıdaki bağlam bilgileri mevcut oturum hakkındadır. " +
                    $"Bu bilgileri yanıtlarınızda dikkate alın:\n\n{context}"));
            }
        }

        if (reasoning != null)
        {
            var hint = BuildReasoningSummaryHint(reasoning);
            if (!string.IsNullOrWhiteSpace(hint))
            {
                messages.Add(new ChatMessage(ChatRole.System, hint));
            }
        }

        var extractedIds = IdExtractor.Extract(query);
        var entityHint = IdExtractor.BuildHintMessage(extractedIds);
        if (!string.IsNullOrWhiteSpace(entityHint))
        {
            messages.Add(new ChatMessage(ChatRole.System, entityHint));
        }

        if (conversationHistory is { Count: > 0 })
            messages.AddRange(conversationHistory.Select(m => new ChatMessage(ToChatRole(m.Role), m.Text)));

        if (session?.State.ForceReplanNextTurn == true)
        {
            var hint = WellKnown.FallbackMessages.ReplanPlanningHint;
            if (!string.IsNullOrWhiteSpace(session.State.ReplanNote))
            {
                hint += $"\n\n📌 Admin notu (sadece sana, müşteri görmez): \"{session.State.ReplanNote}\"";
            }
            messages.Add(new ChatMessage(ChatRole.System, hint));
            session.State.ForceReplanNextTurn = false;
            session.State.ReplanNote = null;
        }

        messages.Add(new ChatMessage(ChatRole.User, query));

        return messages;
    }

    private string BuildReasoningSummaryHint(ReasoningResult r)
    {
        var linesBuilder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(r.Analysis))
            linesBuilder.AppendLine($"- Analiz: {r.Analysis}");
        if (!string.IsNullOrWhiteSpace(r.Intent) && r.Intent != WellKnown.Intents.Unknown)
            linesBuilder.AppendLine($"- Ön-tahmin edilen niyet: {r.Intent}");
        if (r.Steps.Count > 0)
            linesBuilder.AppendLine($"- Önerilen adımlar: {string.Join(" → ", r.Steps.Select(s => s.Description))}");
        if (r.RequiredInfo.Count > 0)
            linesBuilder.AppendLine($"- Gerekli olduğu tahmin edilen bilgiler: {string.Join(", ", r.RequiredInfo)}");
        if (!string.IsNullOrWhiteSpace(r.NextAction))
            linesBuilder.AppendLine($"- Önerilen sonraki aksiyon: {r.NextAction}");

        if (r.SubTasks.Count >= 2)
        {
            linesBuilder.AppendLine(
                $"- ⚠️ COMPOUND QUERY: {r.SubTasks.Count} alt göreve ayrıştırıldı. " +
                "PlanningAgent olarak her birini SIRAYLA aynı yanıtta yönlendir:");
            foreach (var sub in r.SubTasks.OrderBy(s => s.Order))
            {
                var entStr = sub.Entities.Count > 0
                    ? $" [{string.Join(", ", sub.Entities.Select(kv => $"{kv.Key}={kv.Value}"))}]"
                    : "";
                linesBuilder.AppendLine(
                    $"    {sub.Order}. {sub.TargetAgent}: {sub.Description}{entStr}");
            }
        }

        var reasoningLines = linesBuilder.ToString().TrimEnd();
        return _prompts.Render("services/reasoning-hint", new Dictionary<string, string?>
        {
            ["REASONING_LINES"] = reasoningLines
        });
    }

    private async Task<string> RewriteRoutingMessageAsync(string routingMessage, string originalQuery)
    {
        try
        {
            var prompt = new List<ChatMessage>
            {
                new(ChatRole.System, _prompts.Get("services/routing-rewrite-system")),
                new(ChatRole.User, _prompts.Render(
                    "services/routing-rewrite-user",
                    new Dictionary<string, string?>
                    {
                        ["ORIGINAL_QUERY"] = originalQuery,
                        ["ROUTING_MESSAGE"] = routingMessage
                    }))
            };

            var response = await _chatClient.GetResponseAsync(prompt);
            return response.Text ?? WellKnown.FallbackMessages.RoutingRewrite;
        }
        catch
        {
            return WellKnown.FallbackMessages.RoutingRewrite;
        }
    }

    private async Task<string> RunDecomposedAsync(
        string query,
        List<ConversationMessage>? conversationHistory,
        AgentSession? session,
        ReasoningResult reasoning)
    {
        var parts = new List<string>();
        var runningHistory = conversationHistory != null
            ? new List<ConversationMessage>(conversationHistory)
            : new List<ConversationMessage>();

        runningHistory.Add(new ConversationMessage(ConversationRoles.User, query));

        var groups = SubTaskOrchestrator.Partition(reasoning.SubTasks, _parallelOptions);
        var collected = new SortedDictionary<int, string>();

        foreach (var group in groups)
        {
            if (group.Parallel && group.Items.Count > 1)
            {
                using var sem = new SemaphoreSlim(
                    Math.Max(1, _parallelOptions.MaxDegreeOfParallelism));
                var historySnapshot = runningHistory.ToList();

                var tasks = group.Items.Select(async sub =>
                {
                    await sem.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        var subQuery = SubTaskOrchestrator.FormatSubTaskQuery(sub);
                        var subReasoning = SubTaskOrchestrator.CreateSubTaskReasoning(reasoning, sub);
                        var subResp = await RunAsync(subQuery, historySnapshot, session, subReasoning)
                            .ConfigureAwait(false);
                        return (sub, subResp);
                    }
                    finally { sem.Release(); }
                });

                var results = await Task.WhenAll(tasks).ConfigureAwait(false);

                foreach (var (sub, resp) in results.OrderBy(t => t.sub.Order))
                {
                    collected[sub.Order] = SubTaskOrchestrator.FormatSubTaskResult(sub, resp);
                    runningHistory.Add(new ConversationMessage(ConversationRoles.User,
                        SubTaskOrchestrator.FormatSubTaskQuery(sub)));
                    runningHistory.Add(new ConversationMessage(ConversationRoles.Assistant, resp));
                }
            }
            else
            {
                foreach (var sub in group.Items)
                {
                    var subQuery = SubTaskOrchestrator.FormatSubTaskQuery(sub);
                    var subReasoning = SubTaskOrchestrator.CreateSubTaskReasoning(reasoning, sub);
                    var subResp = await RunAsync(subQuery, runningHistory, session, subReasoning)
                        .ConfigureAwait(false);
                    collected[sub.Order] = SubTaskOrchestrator.FormatSubTaskResult(sub, subResp);
                    runningHistory.Add(new ConversationMessage(ConversationRoles.User, subQuery));
                    runningHistory.Add(new ConversationMessage(ConversationRoles.Assistant, subResp));
                }
            }
        }

        return SubTaskOrchestrator.AggregateSubTaskResults(collected.Values.ToList());
    }

    private async IAsyncEnumerable<StreamEvent> RunDecomposedStreamingAsync(
        string query,
        List<ConversationMessage>? conversationHistory,
        AgentSession? session,
        ReasoningResult reasoning,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var runningHistory = conversationHistory != null
            ? new List<ConversationMessage>(conversationHistory)
            : new List<ConversationMessage>();
        runningHistory.Add(new ConversationMessage(ConversationRoles.User, query));

        var collected = new SortedDictionary<int, string>();
        var total = reasoning.SubTasks.Count;
        var groups = SubTaskOrchestrator.Partition(reasoning.SubTasks, _parallelOptions);
        var parallelGroupCount = groups.Count(g => g.Parallel && g.Items.Count > 1);

        yield return new StreamEvent(StreamEventTypes.Agent,
            new
            {
                name = "Orchestrator",
                status = "decomposing",
                subTaskCount = total,
                groupCount = groups.Count,
                parallelGroups = parallelGroupCount
            });

        foreach (var group in groups)
        {
            if (group.Parallel && group.Items.Count > 1)
            {
                foreach (var sub in group.Items)
                {
                    yield return new StreamEvent(StreamEventTypes.Agent,
                        new
                        {
                            name = $"SubTask#{sub.Order}",
                            status = "running",
                            description = sub.Description,
                            targetAgent = sub.TargetAgent,
                            order = sub.Order,
                            total,
                            parallel = true
                        });
                }

                var historySnapshot = runningHistory.ToList();
                using var sem = new SemaphoreSlim(
                    Math.Max(1, _parallelOptions.MaxDegreeOfParallelism));

                var tasks = group.Items.Select(async sub =>
                {
                    await sem.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        var subQuery = SubTaskOrchestrator.FormatSubTaskQuery(sub);
                        var subReasoning = SubTaskOrchestrator.CreateSubTaskReasoning(reasoning, sub);
                        var resp = await RunAsync(subQuery, historySnapshot, session, subReasoning)
                            .ConfigureAwait(false);
                        return (sub, resp);
                    }
                    finally { sem.Release(); }
                }).ToList();

                var results = await Task.WhenAll(tasks).ConfigureAwait(false);

                foreach (var (sub, resp) in results.OrderBy(t => t.sub.Order))
                {
                    collected[sub.Order] = SubTaskOrchestrator.FormatSubTaskResult(sub, resp);
                    runningHistory.Add(new ConversationMessage(ConversationRoles.User,
                        SubTaskOrchestrator.FormatSubTaskQuery(sub)));
                    runningHistory.Add(new ConversationMessage(ConversationRoles.Assistant, resp));

                    yield return new StreamEvent(StreamEventTypes.Agent,
                        new { name = $"SubTask#{sub.Order}", status = "done", order = sub.Order });
                }
            }
            else
            {
                foreach (var sub in group.Items)
                {
                    yield return new StreamEvent(StreamEventTypes.Agent,
                        new
                        {
                            name = $"SubTask#{sub.Order}",
                            status = "running",
                            description = sub.Description,
                            targetAgent = sub.TargetAgent,
                            order = sub.Order,
                            total,
                            parallel = false
                        });

                    var subQuery = SubTaskOrchestrator.FormatSubTaskQuery(sub);
                    var subReasoning = SubTaskOrchestrator.CreateSubTaskReasoning(reasoning, sub);
                    var subResponseBuilder = new StringBuilder();

                    await foreach (var evt in RunStreamingAsync(
                        subQuery, runningHistory, session, subReasoning, ct))
                    {
                        switch (evt.Type)
                        {
                            case var t when t == StreamEventTypes.ResponseDelta:
                                subResponseBuilder.Append(WorkflowResponseExtractor.ExtractDeltaText(evt.Data));
                                break;
                            case var t when t == StreamEventTypes.ResponseStart
                                         || t == StreamEventTypes.ResponseComplete:
                                break;
                            case var t when t == StreamEventTypes.ReasoningStart
                                         || t == StreamEventTypes.ReasoningDelta
                                         || t == StreamEventTypes.ReasoningComplete:
                                break;
                            case var t when t == StreamEventTypes.Error:
                                yield return evt;
                                break;
                            default:
                                yield return evt;
                                break;
                        }
                    }

                    var subResponse = subResponseBuilder.ToString().Trim();
                    collected[sub.Order] = SubTaskOrchestrator.FormatSubTaskResult(sub, subResponse);
                    runningHistory.Add(new ConversationMessage(ConversationRoles.User, subQuery));
                    runningHistory.Add(new ConversationMessage(ConversationRoles.Assistant, subResponse));

                    yield return new StreamEvent(StreamEventTypes.Agent,
                        new { name = $"SubTask#{sub.Order}", status = "done", order = sub.Order });
                }
            }
        }

        yield return new StreamEvent(StreamEventTypes.Agent,
            new { name = "Orchestrator", status = "aggregating" });

        var aggregated = SubTaskOrchestrator.AggregateSubTaskResults(collected.Values.ToList());

        yield return new StreamEvent(StreamEventTypes.ResponseStart,
            new
            {
                terminationReason = WellKnown.Termination.ReasonCompleted,
                revised = false,
                decomposed = true,
                subTaskCount = total
            });

        await foreach (var chunk in WorkflowResponseExtractor.StreamTextInChunksAsync(aggregated, ct))
        {
            yield return new StreamEvent(StreamEventTypes.ResponseDelta, new { text = chunk });
        }

        yield return new StreamEvent(StreamEventTypes.ResponseComplete,
            new
            {
                text = aggregated,
                terminationReason = WellKnown.Termination.ReasonCompleted,
                revised = false,
                decomposed = true,
                subTaskCount = total
            });
    }

    private static async IAsyncEnumerable<(WorkflowEvent? evt, string? error)>
        EnumerateWorkflowEventsSafely(
            StreamingRun run,
            [EnumeratorCancellation] CancellationToken ct)
    {
        var enumerator = run.WatchStreamAsync().WithCancellation(ct).GetAsyncEnumerator();
        try
        {
            while (true)
            {
                WorkflowEvent? current = null;
                string? error = null;
                try
                {
                    if (!await enumerator.MoveNextAsync()) yield break;
                    current = enumerator.Current;
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                yield return (current, error);
                if (error != null) yield break;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    private static ChatRole ToChatRole(string role) => role switch
    {
        ConversationRoles.User   => ChatRole.User,
        ConversationRoles.System => ChatRole.System,
        _                        => ChatRole.Assistant
    };
}

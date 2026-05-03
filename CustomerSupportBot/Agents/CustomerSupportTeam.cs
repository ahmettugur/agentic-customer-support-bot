// Agents/CustomerSupportTeam.cs
// Müşteri destek ajan takımını yönetir.
// Ajan oluşturma + workflow execution + streaming delegasyonu.
//
// Mimari:
// 1. PlanningAgent       → Yönlendirme (araç yok)
// 2. ProductInquiryAgent → product_inquiry_tool
// 3. OrderPlacementAgent → order_placement_tool (HITL approval gate)
// 4. OrderInquiryAgent   → order_status_tool
// 5. ComplaintAgent      → complaint_registration_tool (HITL approval gate)
// 6. HumanHandoffAgent   → human_handoff_tool
// 7. ResponseAgent       → Son yanıt biçimlendirme, "TERMINATE" ile sonlandırma

using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using CustomerSupportBot.Models;
using CustomerSupportBot.Services;
using CustomerSupportBot.Tools;

namespace CustomerSupportBot.Agents;

/// <summary>
/// Müşteri destek ajan takımını yönetir.
/// Ajanları oluşturur, workflow'u başlatır ve sonuçları işler.
/// </summary>
public class CustomerSupportTeam : ICustomerSupportTeam
{
    // Ajanlar bir kez oluşturulur (stateless) — workflow ise her istek için taze üretilir
    private readonly ChatClientAgent _planningAgent;
    private readonly ChatClientAgent _productInquiryAgent;
    private readonly ChatClientAgent _orderPlacementAgent;
    private readonly ChatClientAgent _orderInquiryAgent;
    private readonly ChatClientAgent _complaintAgent;
    private readonly ChatClientAgent _humanHandoffAgent;
    private readonly ChatClientAgent _responseAgent;
    private readonly ContextPipeline _contextPipeline;
    private readonly IChatClient _chatClient;
    private readonly WorkflowGuardOptions _guards;
    private readonly IReasoningTraceStore _traceStore;
    private readonly RevisionService _revisionService;
    private readonly PromptService _prompts;
    private readonly ApprovalGateService _approvalGate;
    private readonly ILoggerFactory _loggerFactory;

    public CustomerSupportTeam(
        IChatClient chatClient,
        ContextPipeline contextPipeline,
        IConfiguration configuration,
        IReasoningTraceStore traceStore,
        RevisionService revisionService,
        PromptService prompts,
        ApprovalGateService approvalGate,
        ILoggerFactory loggerFactory)
    {
        _contextPipeline = contextPipeline;
        _chatClient = chatClient;
        _traceStore = traceStore;
        _revisionService = revisionService;
        _prompts = prompts;
        _approvalGate = approvalGate;
        _loggerFactory = loggerFactory;

        // Guard ayarlarını appsettings.json'dan oku
        _guards = new WorkflowGuardOptions();
        configuration.GetSection("WorkflowGuards").Bind(_guards);

        // ─── AJANLARI OLUŞTUR ───

        _planningAgent = new ChatClientAgent(
            chatClient,
            instructions: _prompts.Get("agents/planning-agent"),
            name: WellKnown.AgentNames.Planning,
            description: "Müşteri destek görevlerini planlayan ve uygun ajanlara yönlendiren bir ajandır.");

        _productInquiryAgent = new ChatClientAgent(
            chatClient,
            instructions: _prompts.Get("agents/product-inquiry-agent"),
            name: WellKnown.AgentNames.ProductInquiry,
            description: "Ürün sorgularını yanıtlar.",
            tools: [AIFunctionFactory.Create(CustomerSupportTools.ProductInquiryTool)]);

        _orderPlacementAgent = new ChatClientAgent(
            chatClient,
            instructions: _prompts.Get("agents/order-placement-agent"),
            name: WellKnown.AgentNames.OrderPlacement,
            description: "Handles order placement.",
            tools: [_approvalGate.BuildOrderPlacementTool()]);

        _orderInquiryAgent = new ChatClientAgent(
            chatClient,
            instructions: _prompts.Get("agents/order-inquiry-agent"),
            name: WellKnown.AgentNames.OrderInquiry,
            description: "Sipariş durumu sorgularını yanıtlar.",
            tools: [
                AIFunctionFactory.Create(CustomerSupportTools.OrderStatusTool),
                AIFunctionFactory.Create(CustomerSupportTools.GetLastOrderTool),
                AIFunctionFactory.Create(CustomerSupportTools.GetAllOrdersTool)
            ]);

        _complaintAgent = new ChatClientAgent(
            chatClient,
            instructions: _prompts.Get("agents/complaint-agent"),
            name: WellKnown.AgentNames.Complaint,
            description: "Müşteri şikayetlerini işler.",
            tools: [_approvalGate.BuildComplaintRegistrationTool()]);

        _humanHandoffAgent = new ChatClientAgent(
            chatClient,
            instructions: _prompts.Get("agents/human-handoff-agent"),
            name: WellKnown.AgentNames.HumanHandoff,
            description: "Kullanıcının açıkça insan temsilcisiyle görüşme talebini karşılar.",
            tools: [AIFunctionFactory.Create(CustomerSupportTools.HumanHandoffTool)]);

        _responseAgent = new ChatClientAgent(
            chatClient,
            instructions: _prompts.Get("agents/response-agent"),
            name: WellKnown.AgentNames.Response,
            description: "Yanıtları biçimlendirir ve kullanıcıya iletir.");
    }

    /// <summary>
    /// Her istek için yeni bir Workflow instance'ı oluşturur.
    /// </summary>
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
                _productInquiryAgent,
                _orderPlacementAgent,
                _orderInquiryAgent,
                _complaintAgent,
                _humanHandoffAgent,
                _responseAgent)
            .Build();
    }

    /// <summary>
    /// Kullanıcı sorgusunu grup sohbet iş akışında çalıştırır ve son yanıtı döndürür.
    /// </summary>
    public async Task<string> RunAsync(
        string query,
        List<ChatMessage>? conversationHistory = null,
        Models.AgentSession? session = null,
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
                    throw new InvalidOperationException(
                        $"Workflow hatası: {errorEvt.Exception?.Message}",
                        errorEvt.Exception);
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

    /// <summary>
    /// Workflow'u streaming olarak çalıştırır.
    /// </summary>
    public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(
        string query,
        List<ChatMessage>? conversationHistory = null,
        Models.AgentSession? session = null,
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

                    var visit = new AgentVisit
                    {
                        AgentName = executorId,
                        StartedAt = DateTime.UtcNow
                    };
                    activeVisits[executorId] = visit;
                    trace.AgentVisits.Add(visit);
                    _traceStore.Update(trace);

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
                        _traceStore.Update(trace);
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
                    var finalCritique = WorkflowResponseExtractor.ExtractFinalCritiqueFromOutput(output);
                    if (finalCritique != null) trace.FinalCritique = finalCritique;
                    if (planning != null || specialistReasonings.Count > 0 || finalCritique != null)
                        _traceStore.Update(trace);
                    break;

                case WorkflowErrorEvent errorEvt:
                    workflowError = errorEvt.Exception?.Message ?? "workflow error";
                    break;
            }

            if (workflowError != null) break;
        }

        trace.IterationCount = iterationCount;

        // Timeout kontrolü
        if (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _traceStore.Complete(trace.TraceId,
                terminationReason: WellKnown.Termination.ReasonTimeout,
                error: $"Workflow {_guards.TimeoutSeconds}s timeout'a takıldı");
            yield return new StreamEvent(StreamEventTypes.Error,
                new { message = $"İşlem {_guards.TimeoutSeconds} saniyede tamamlanamadı." });
            yield break;
        }

        // Workflow hatası
        if (workflowError != null)
        {
            _traceStore.Complete(trace.TraceId, terminationReason: "error", error: workflowError);
            yield return new StreamEvent(StreamEventTypes.Error,
                new { message = workflowError });
            yield break;
        }

        // TERMINATE suffix'ten reason'u parse et, sonra temizle
        var terminationReason = WorkflowResponseExtractor.ParseTerminationReasonFromResult(result) ?? WellKnown.Termination.ReasonCompleted;
        result = WorkflowResponseExtractor.RemoveTerminationMarkers(result);
        result = WorkflowResponseExtractor.RemoveTechnicalJsonBlocks(result);

        // Routing mesajı fallback
        if (WorkflowResponseExtractor.ContainsAgentRoutingMessage(result))
            result = await RewriteRoutingMessageAsync(result, query);

        // Revizyon kontrolü
        if (RevisionService.ShouldRevise(trace.FinalCritique))
        {
            trace.FirstDraftResponse = result;
            trace.WasRevised = true;

            yield return new StreamEvent(StreamEventTypes.Agent,
                new { name = "RevisionAgent", status = "running" });

            var revised = await _revisionService.ReviseAsync(
                query, result, trace.FinalCritique!, effectiveCt);

            yield return new StreamEvent(StreamEventTypes.Agent,
                new { name = "RevisionAgent", status = "done" });

            revised = WorkflowResponseExtractor.RemoveTerminationMarkers(revised);
            revised = WorkflowResponseExtractor.RemoveTechnicalJsonBlocks(revised);
            result = revised;

            _traceStore.Update(trace);
        }

        // HITL — escalation tespiti
        _approvalGate.ProcessPendingEscalations(trace, query, result);

        // Trace'i tamamla
        _traceStore.Complete(trace.TraceId,
            terminationReason: terminationReason,
            finalResponse: result);

        // Response stream
        yield return new StreamEvent(StreamEventTypes.ResponseStart,
            new { terminationReason, revised = trace.WasRevised });

        await foreach (var chunk in WorkflowResponseExtractor.StreamTextInChunksAsync(result, effectiveCt))
        {
            yield return new StreamEvent(StreamEventTypes.ResponseDelta, new { text = chunk });
        }

        yield return new StreamEvent(StreamEventTypes.ResponseComplete,
            new { text = result, terminationReason, revised = trace.WasRevised });
    }

    // ════════════════════════════════════════════════════════════════
    // Workflow mesaj oluşturma
    // ════════════════════════════════════════════════════════════════

    private async Task<List<ChatMessage>> BuildWorkflowMessagesAsync(
        string query,
        List<ChatMessage>? conversationHistory,
        Models.AgentSession? session,
        ReasoningResult? reasoning)
    {
        var messages = new List<ChatMessage>();

        // Context Provider'lardan bağlam bilgisi al (async — sync-over-async anti-pattern düzeltildi)
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

        // Ön-analiz reasoning'ini PlanningAgent'a ilet
        if (reasoning != null)
        {
            var hint = BuildReasoningSummaryHint(reasoning);
            if (!string.IsNullOrWhiteSpace(hint))
            {
                messages.Add(new ChatMessage(ChatRole.System, hint));
            }
        }

        // Entity extraction — regex ile deterministik ID çıkarımı
        var extractedIds = IdExtractor.Extract(query);
        var entityHint = IdExtractor.BuildHintMessage(extractedIds);
        if (!string.IsNullOrWhiteSpace(entityHint))
        {
            messages.Add(new ChatMessage(ChatRole.System, entityHint));
        }

        // Önceki konuşma geçmişi
        if (conversationHistory is { Count: > 0 })
            messages.AddRange(conversationHistory);

        // Admin Replan override: müşterinin sonraki mesajında PlanningAgent'a
        // geçmişi göz ardı etmesi yönergesi (one-shot — flag tek seferlik kullanılır).
        if (session?.State.ForceReplanNextTurn == true)
        {
            var hint = WellKnown.FallbackMessages.ReplanPlanningHint;
            if (!string.IsNullOrWhiteSpace(session.State.ReplanNote))
            {
                hint += $"\n\n📌 Admin notu (sadece sana, müşteri görmez): \"{session.State.ReplanNote}\"";
            }
            messages.Add(new ChatMessage(ChatRole.System, hint));
            session.State.ForceReplanNextTurn = false;
            session.State.ReplanNote = null; // one-shot temizle
        }

        // Yeni kullanıcı mesajı
        messages.Add(new ChatMessage(ChatRole.User, query));

        return messages;
    }

    /// <summary>
    /// ReasoningService çıktısını PlanningAgent'a "ön-analiz" olarak iletmek için özet üretir.
    /// </summary>
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

        // Compound query decomposition
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

    // ════════════════════════════════════════════════════════════════
    // Routing mesaj yeniden yazma
    // ════════════════════════════════════════════════════════════════

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

    // ════════════════════════════════════════════════════════════════
    // Compound query decomposition orkestrasyonu
    // ════════════════════════════════════════════════════════════════

    private async Task<string> RunDecomposedAsync(
        string query,
        List<ChatMessage>? conversationHistory,
        Models.AgentSession? session,
        ReasoningResult reasoning)
    {
        var parts = new List<string>();
        var runningHistory = conversationHistory != null
            ? new List<ChatMessage>(conversationHistory)
            : new List<ChatMessage>();

        runningHistory.Add(new ChatMessage(ChatRole.User, query));

        foreach (var subTask in reasoning.SubTasks.OrderBy(s => s.Order))
        {
            var subQuery = SubTaskOrchestrator.FormatSubTaskQuery(subTask);
            var subReasoning = SubTaskOrchestrator.CreateSubTaskReasoning(reasoning, subTask);

            var subResponse = await RunAsync(subQuery, runningHistory, session, subReasoning);
            parts.Add(SubTaskOrchestrator.FormatSubTaskResult(subTask, subResponse));

            runningHistory.Add(new ChatMessage(ChatRole.User, subQuery));
            runningHistory.Add(new ChatMessage(ChatRole.Assistant, subResponse));
        }

        return SubTaskOrchestrator.AggregateSubTaskResults(parts);
    }

    private async IAsyncEnumerable<StreamEvent> RunDecomposedStreamingAsync(
        string query,
        List<ChatMessage>? conversationHistory,
        Models.AgentSession? session,
        ReasoningResult reasoning,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var runningHistory = conversationHistory != null
            ? new List<ChatMessage>(conversationHistory)
            : new List<ChatMessage>();
        runningHistory.Add(new ChatMessage(ChatRole.User, query));

        var parts = new List<string>();
        var total = reasoning.SubTasks.Count;

        yield return new StreamEvent(StreamEventTypes.Agent,
            new { name = "Orchestrator", status = "decomposing", subTaskCount = total });

        var index = 0;
        foreach (var subTask in reasoning.SubTasks.OrderBy(s => s.Order))
        {
            index++;
            var subQuery = SubTaskOrchestrator.FormatSubTaskQuery(subTask);
            var subReasoning = SubTaskOrchestrator.CreateSubTaskReasoning(reasoning, subTask);

            yield return new StreamEvent(StreamEventTypes.Agent,
                new
                {
                    name = $"SubTask#{index}",
                    status = "running",
                    description = subTask.Description,
                    targetAgent = subTask.TargetAgent,
                    order = subTask.Order,
                    total
                });

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
            parts.Add(SubTaskOrchestrator.FormatSubTaskResult(subTask, subResponse));
            runningHistory.Add(new ChatMessage(ChatRole.User, subQuery));
            runningHistory.Add(new ChatMessage(ChatRole.Assistant, subResponse));

            yield return new StreamEvent(StreamEventTypes.Agent,
                new { name = $"SubTask#{index}", status = "done", order = subTask.Order });
        }

        yield return new StreamEvent(StreamEventTypes.Agent,
            new { name = "Orchestrator", status = "aggregating" });

        var aggregated = SubTaskOrchestrator.AggregateSubTaskResults(parts);

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

    // ════════════════════════════════════════════════════════════════
    // Workflow event safe enumeration
    // ════════════════════════════════════════════════════════════════

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
}

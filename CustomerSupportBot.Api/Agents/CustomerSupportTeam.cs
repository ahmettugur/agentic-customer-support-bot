// Agents/CustomerSupportTeam.cs
// Müşteri destek ajan takımını yönetir.
// Ajan oluşturma + workflow execution + streaming delegasyonu.
//
// Mimari:
// 1. PlanningAgent       → Yönlendirme (araç yok)
// 2. ProductInquiryAgent → product_inquiry_tool
// 3. OrderAgent          → order_placement_tool (HITL) + order_status_tool + get_last_order_tool + get_all_orders_tool
// 4. ComplaintAgent      → complaint_registration_tool (HITL approval gate)
// 5. HumanHandoffAgent   → human_handoff_tool
// 6. ResponseAgent       → Son yanıt biçimlendirme, "TERMINATE" ile sonlandırma

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using CustomerSupportBot.Domain.Model;
using AgentSession = CustomerSupportBot.Domain.Model.AgentSession;
using CustomerSupportBot.Domain.Services;
using CustomerSupportBot.Api.Services;
using CustomerSupportBot.Api.Services.Memory;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Api.Agents;

/// <summary>
/// Müşteri destek ajan takımını yönetir.
/// Ajanları oluşturur, workflow'u başlatır ve sonuçları işler.
/// </summary>
public class CustomerSupportTeam : ICustomerSupportTeam
{
    // Ajanlar bir kez oluşturulur (stateless) — workflow ise her istek için taze üretilir.
    // Tip AIAgent: her agent OpenTelemetry middleware ile sarıldığı için concrete tip ChatClientAgent değildir.
    private readonly AIAgent _planningAgent;
    private readonly AIAgent _productInquiryAgent;
    private readonly AIAgent _orderAgent;
    private readonly AIAgent _complaintAgent;
    private readonly AIAgent _humanHandoffAgent;
    private readonly AIAgent _responseAgent;
    private readonly ContextPipeline _contextPipeline;
    private readonly IChatClient _chatClient;
    private readonly WorkflowGuardOptions _guards;
    private readonly IReasoningTraceStore _traceStore;
    private readonly PromptService _prompts;
    private readonly ApprovalGateService _approvalGate;
    private readonly CustomerSupportToolsService _tools;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SemanticMemoryService? _semanticMemory;
    private readonly Services.Personalization.CustomerProfileService? _profileService;
    private readonly ParallelExecutionOptions _parallelOptions;

    public CustomerSupportTeam(
        IChatClient chatClient,
        ContextPipeline contextPipeline,
        IConfiguration configuration,
        IReasoningTraceStore traceStore,
        PromptService prompts,
        ApprovalGateService approvalGate,
        CustomerSupportToolsService tools,
        ILoggerFactory loggerFactory,
        SemanticMemoryService? semanticMemory = null,
        Services.Personalization.CustomerProfileService? profileService = null)
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

        // Guard ayarlarını appsettings.json'dan oku
        _guards = new WorkflowGuardOptions();
        configuration.GetSection("WorkflowGuards").Bind(_guards);

        // Parallel sub-task execution ayarları (#E)
        _parallelOptions = new ParallelExecutionOptions();
        configuration.GetSection(ParallelExecutionOptions.SectionName).Bind(_parallelOptions);

        // ─── AJANLARI OLUŞTUR ───

        // Her agent OpenTelemetry middleware ile sarılarak otomatik agent.run / tool.invoke
        // span'ları üretir; CustomerSupportTelemetry.ActivitySource ile aynı source adı üzerinde
        // toplanır ve yapılandırılan exporter'a (OTLP/Console) akar.
        var sourceName = Services.Telemetry.CustomerSupportTelemetry.ActivitySourceName;

        _planningAgent = WrapWithTelemetry(new ChatClientAgent(
            chatClient,
            instructions: _prompts.Get("agents/planning-agent"),
            name: WellKnown.AgentNames.Planning,
            description: "Müşteri destek görevlerini planlayan ve uygun ajanlara yönlendiren bir ajandır."), sourceName);

        _productInquiryAgent = WrapWithTelemetry(new ChatClientAgent(
            chatClient,
            instructions: _prompts.Get("agents/product-inquiry-agent"),
            name: WellKnown.AgentNames.ProductInquiry,
            description: "Ürün sorgularını yanıtlar.",
            tools: [AIFunctionFactory.Create(_tools.ProductInquiryTool)]), sourceName);

        _orderAgent = WrapWithTelemetry(new ChatClientAgent(
            chatClient,
            instructions: _prompts.Get("agents/order-agent"),
            name: WellKnown.AgentNames.Order,
            description: "Sipariş oluşturma ve sorgulama işlemlerini yürütür.",
            tools: [
                _approvalGate.BuildOrderPlacementTool(),
                AIFunctionFactory.Create(_tools.OrderStatusTool),
                AIFunctionFactory.Create(_tools.GetLastOrderTool),
                AIFunctionFactory.Create(_tools.GetAllOrdersTool)
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
            tools: [AIFunctionFactory.Create(CustomerSupportToolsService.HumanHandoffTool)]), sourceName);

        _responseAgent = WrapWithTelemetry(new ChatClientAgent(
            chatClient,
            instructions: _prompts.Get("agents/response-agent"),
            name: WellKnown.AgentNames.Response,
            description: "Yanıtları biçimlendirir ve kullanıcıya iletir."), sourceName);
    }

    /// <summary>
    /// Bir <see cref="ChatClientAgent"/>'ı OpenTelemetry middleware ile sarmalar.
    /// Her agent.run ve tool çağrısı, verilen ActivitySource adı altında otomatik span üretir.
    /// </summary>
    private static AIAgent WrapWithTelemetry(ChatClientAgent agent, string sourceName)
        => agent.AsBuilder().UseOpenTelemetry(sourceName).Build();

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
                _orderAgent,
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

        // HITL — escalation tespiti
        _approvalGate.ProcessPendingEscalations(trace, query, result);

        // AgentVisit.Output alanlarını trace'in zenginleştirilmiş verisinden doldur.
        PopulateAgentVisitOutputs(trace, result);

        // Trace'i tamamla

        // Episodic memory — fire & forget (kullanıcıya yanıt akışını bloklama)
        WriteEpisodicMemorySafe(trace, query, result);

        // Per-customer profile — heuristic update (LLM-siz, ucuz)
        await UpdateCustomerProfileSafeAsync(session, trace, query, result);

        _traceStore.Complete(trace.TraceId,
            terminationReason: terminationReason,
            finalResponse: result);

        // Response stream
        yield return new StreamEvent(StreamEventTypes.ResponseStart,
            new { terminationReason });

        await foreach (var chunk in WorkflowResponseExtractor.StreamTextInChunksAsync(result, effectiveCt))
        {
            yield return new StreamEvent(StreamEventTypes.ResponseDelta, new { text = chunk });
        }

        yield return new StreamEvent(StreamEventTypes.ResponseComplete,
            new { text = result, terminationReason });
    }

    // ════════════════════════════════════════════════════════════════
    // Episodic memory yazımı
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Workflow framework, ExecutorCompleted event'inde agent'ın ürettiği metni
    /// taşımıyor. Bu yüzden trace tamamlanırken agent çıktılarını,
    /// trace'in zenginleştirilmiş verisinden post-hoc olarak doldururuz:
    ///   - PlanningAgent_*    → trace.Planning (JSON özeti)
    ///   - *Agent_*  (specialist) → eşleşen SpecialistReasoning JSON'u
    ///   - ResponseAgent_*    → final response (truncate)
    /// </summary>
    private static void PopulateAgentVisitOutputs(ReasoningTrace trace, string finalResult)
    {
        const int MaxLen = 1500;
        static string Truncate(string s) => s.Length <= MaxLen ? s : s[..MaxLen] + "…";

        foreach (var visit in trace.AgentVisits)
        {
            if (!string.IsNullOrWhiteSpace(visit.Output)) continue;

            var name = visit.AgentName ?? "";
            // executorId tipik olarak "AgentName_<hash>" veya bare "AgentName" şeklinde gelir.
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
                // Aynı baseName'e sahip specialist reasoning(ler)i topla.
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
            // Distributed lock ile per-customer serialize — ucuz (LLM çağırmaz).
            // exception bile olsa response stream'i bloklamasın diye try/catch.
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

    // ════════════════════════════════════════════════════════════════
    // Workflow mesaj oluşturma
    // ════════════════════════════════════════════════════════════════

    private async Task<List<ChatMessage>> BuildWorkflowMessagesAsync(
        string query,
        List<ChatMessage>? conversationHistory,
        AgentSession? session,
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
        AgentSession? session,
        ReasoningResult reasoning)
    {
        var parts = new List<string>();
        var runningHistory = conversationHistory != null
            ? new List<ChatMessage>(conversationHistory)
            : new List<ChatMessage>();

        runningHistory.Add(new ChatMessage(ChatRole.User, query));

        var groups = SubTaskOrchestrator.Partition(reasoning.SubTasks, _parallelOptions);
        var collected = new SortedDictionary<int, string>();

        foreach (var group in groups)
        {
            if (group.Parallel && group.Items.Count > 1)
            {
                // Yan-etkisiz alt görevleri paralel çalıştır.
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

                // Order'a göre sırala — paralel batch'te de output deterministic kalsın
                foreach (var (sub, resp) in results.OrderBy(t => t.sub.Order))
                {
                    collected[sub.Order] = SubTaskOrchestrator.FormatSubTaskResult(sub, resp);
                    runningHistory.Add(new ChatMessage(ChatRole.User,
                        SubTaskOrchestrator.FormatSubTaskQuery(sub)));
                    runningHistory.Add(new ChatMessage(ChatRole.Assistant, resp));
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
                    runningHistory.Add(new ChatMessage(ChatRole.User, subQuery));
                    runningHistory.Add(new ChatMessage(ChatRole.Assistant, subResp));
                }
            }
        }

        return SubTaskOrchestrator.AggregateSubTaskResults(collected.Values.ToList());
    }

    private async IAsyncEnumerable<StreamEvent> RunDecomposedStreamingAsync(
        string query,
        List<ChatMessage>? conversationHistory,
        AgentSession? session,
        ReasoningResult reasoning,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var runningHistory = conversationHistory != null
            ? new List<ChatMessage>(conversationHistory)
            : new List<ChatMessage>();
        runningHistory.Add(new ChatMessage(ChatRole.User, query));

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
                // Paralel batch — her alt görevin "running" eventini önce yay,
                // sonra hepsini Task.WhenAll ile çalıştır, son olarak Order'a göre
                // sıralı "done" eventleri emit et. Sub-task delta'ları dış stream'e
                // sızdırılmaz (UI karışmasın); aggregate response sonda akıtılır.
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
                    runningHistory.Add(new ChatMessage(ChatRole.User,
                        SubTaskOrchestrator.FormatSubTaskQuery(sub)));
                    runningHistory.Add(new ChatMessage(ChatRole.Assistant, resp));

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
                    runningHistory.Add(new ChatMessage(ChatRole.User, subQuery));
                    runningHistory.Add(new ChatMessage(ChatRole.Assistant, subResponse));

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


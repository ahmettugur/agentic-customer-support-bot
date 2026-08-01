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
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Services;
using CustomerSupportBot.Application.Services.Approval;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Application.Services.Escalation;
using CustomerSupportBot.Application.Services.Reasoning;
using CustomerSupportBot.Application.Services.Tools;
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
    private readonly IUiHintEmitter _uiHint;
    private readonly IApprovalContextAccessor _approvalContext;
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
        IUiHintEmitter uiHint,
        IApprovalContextAccessor approvalContext,
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
        _uiHint = uiHint;
        _approvalContext = approvalContext;
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
        ReasoningResult? reasoning = null,
        CancellationToken ct = default)
    {
        if (SubTaskOrchestrator.IsCompoundQuery(reasoning))
        {
            return await RunDecomposedAsync(query, conversationHistory, session, reasoning!, ct);
        }

        var messages = await BuildWorkflowMessagesAsync(query, conversationHistory, session, reasoning);

        using var timeoutCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(_guards.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var effectiveCt = linkedCts.Token;

        var st = StartTraceState(session, query, reasoning);

        var workflow = CreateWorkflow();
        await using var run = await InProcessExecution.RunStreamingAsync(workflow, messages);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        string? workflowError = null;

        await foreach (var (evt, evtError) in EnumerateWorkflowEventsSafely(run, effectiveCt))
        {
            if (evtError != null)
            {
                workflowError = evtError;
                break;
            }
            if (evt == null) continue;

            if (evt is WorkflowErrorEvent errorEvt)
            {
                workflowError = errorEvt.Exception?.Message ?? "workflow error";
                break;
            }

            ApplyTraceEvent(st, evt);
        }

        st.Trace.IterationCount = st.IterationCount;

        if (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _traceStore.Complete(st.Trace.TraceId,
                terminationReason: WellKnown.Termination.ReasonTimeout,
                error: $"Workflow {_guards.TimeoutSeconds}s timeout'a takıldı");
            throw ExceptionTranslator.Translate(
                new TimeoutException($"Workflow {_guards.TimeoutSeconds}s timeout'a takıldı."),
                "RunAsync workflow timeout.");
        }

        if (ct.IsCancellationRequested)
        {
            _traceStore.Complete(st.Trace.TraceId,
                terminationReason: "cancelled",
                error: "İstek çağıran tarafından iptal edildi.");
            ct.ThrowIfCancellationRequested();
        }

        if (workflowError != null)
        {
            _traceStore.Complete(st.Trace.TraceId, terminationReason: "error", error: workflowError);
            throw ExceptionTranslator.Translate(
                new InvalidOperationException(workflowError),
                "RunAsync workflow hatası.");
        }

        var terminationReason =
            WorkflowResponseExtractor.ParseTerminationReasonFromResult(st.Result)
            ?? WellKnown.Termination.ReasonCompleted;

        var result = WorkflowResponseExtractor.RemoveTerminationMarkers(st.Result);
        result = WorkflowResponseExtractor.RemoveTechnicalJsonBlocks(result);

        if (WorkflowResponseExtractor.ContainsAgentRoutingMessage(result))
        {
            result = await RewriteRoutingMessageAsync(result, query, ct);
        }

        await FinalizeTraceAsync(st, session, query, result, terminationReason);

        return result;
    }

    public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(
        string query,
        List<ConversationMessage>? conversationHistory = null,
        AgentSession? session = null,
        ReasoningResult? reasoning = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var sessionId = session?.SessionId ?? string.Empty;
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

        var st = StartTraceState(session, query, reasoning);

        var workflow = CreateWorkflow();
        await using var run = await InProcessExecution.RunStreamingAsync(workflow, messages);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        string? workflowError = null;

        await foreach (var (evt, evtError) in EnumerateWorkflowEventsSafely(run, effectiveCt))
        {
            if (evtError != null)
            {
                workflowError = evtError;
                break;
            }
            if (evt == null) continue;

            if (evt is WorkflowErrorEvent errorEvt)
            {
                workflowError = errorEvt.Exception?.Message ?? "workflow error";
                break;
            }

            // Trace toplama RunAsync ile paylaşılan ApplyTraceEvent'te; burada yalnızca
            // gözlemlenebilir bir ajan durumu değişikliği varsa stream event yayınlanır.
            if (ApplyTraceEvent(st, evt) is { } surfaced)
            {
                yield return new StreamEvent(StreamEventTypes.Agent,
                    new { name = surfaced.Name, status = surfaced.Status });
            }

            // Tool çağrıları sırasında biriken UI ipuçlarını (ör. category_picker) hemen yayınla
            foreach (var hint in _uiHint.DrainPending(sessionId))
                yield return hint;
        }

        st.Trace.IterationCount = st.IterationCount;

        if (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _traceStore.Complete(st.Trace.TraceId,
                terminationReason: WellKnown.Termination.ReasonTimeout,
                error: $"Workflow {_guards.TimeoutSeconds}s timeout'a takıldı");
            yield return new StreamEvent(StreamEventTypes.Error,
                new { message = $"İşlem {_guards.TimeoutSeconds} saniyede tamamlanamadı." });
            yield break;
        }

        if (workflowError != null)
        {
            _traceStore.Complete(st.Trace.TraceId, terminationReason: "error", error: workflowError);
            yield return new StreamEvent(StreamEventTypes.Error,
                new { message = workflowError });
            yield break;
        }

        var terminationReason =
            WorkflowResponseExtractor.ParseTerminationReasonFromResult(st.Result)
            ?? WellKnown.Termination.ReasonCompleted;

        var result = WorkflowResponseExtractor.RemoveTerminationMarkers(st.Result);
        result = WorkflowResponseExtractor.RemoveTechnicalJsonBlocks(result);

        if (WorkflowResponseExtractor.ContainsAgentRoutingMessage(result))
            result = await RewriteRoutingMessageAsync(result, query, effectiveCt);

        await FinalizeTraceAsync(st, session, query, result, terminationReason);

        yield return new StreamEvent(StreamEventTypes.ResponseStart,
            new { terminationReason });

        await foreach (var chunk in WorkflowResponseExtractor.StreamTextInChunksAsync(result, effectiveCt))
        {
            yield return new StreamEvent(StreamEventTypes.ResponseDelta, new { text = chunk });
        }

        yield return new StreamEvent(StreamEventTypes.ResponseComplete,
            new { text = result, terminationReason });
    }

    /// <summary>
    /// Tek bir workflow koşusunun trace toplama durumu. <see cref="RunAsync"/> ve
    /// <see cref="RunStreamingAsync"/> aynı toplama mantığını paylaşır — daha önce
    /// yalnızca streaming yol trace üretiyordu, bu yüzden non-streaming çağıranlar
    /// (POST /chat/, EvaluationRunner, ReplanService) izlenemiyor ve eskalasyon
    /// kaydı oluşturmuyordu.
    /// </summary>
    private sealed class TraceState
    {
        public required ReasoningTrace Trace { get; init; }
        public Dictionary<string, AgentVisit> ActiveVisits { get; } = new();
        public string? LastAgentSignature { get; set; }
        public int IterationCount { get; set; }
        public string Result { get; set; } = "";
    }

    private TraceState StartTraceState(AgentSession? session, string query, ReasoningResult? reasoning)
    {
        var trace = _traceStore.StartTrace(session?.SessionId ?? "anonymous", query);
        if (reasoning != null)
        {
            trace.Reasoning = reasoning;
            _traceStore.Update(trace);
        }
        return new TraceState { Trace = trace };
    }

    /// <summary>
    /// Bir workflow event'inin trace yan etkilerini uygular.
    /// Dönüş değeri null değilse çağıran taraf bunu bir <c>Agent</c> stream event'i
    /// olarak yayınlayabilir; null ise event iç/mükerrer olduğu için gözlemlenebilir
    /// bir değişiklik üretmemiştir.
    /// </summary>
    private (string Name, string Status)? ApplyTraceEvent(TraceState st, WorkflowEvent evt)
    {
        switch (evt)
        {
            case ExecutorInvokedEvent invoked:
            {
                var executorId = invoked.ExecutorId ?? "unknown";
                if (WorkflowResponseExtractor.IsInternalWorkflowExecutor(executorId)) return null;

                // GroupChatHost her turda seçilmeyen tüm ajanlara da geçmişlerini senkron
                // tutmak için mesaj yollar (BroadcastAsync) — bu da Invoked/Completed
                // event çiftini tetikler ama ajan gerçekte çalışmaz. Süreye bakarak ayırt
                // etmek güvenilir değil (gerçek ajanlar da bazen <1ms'de tamamlanabiliyor);
                // asıl ayırt edici framework'ün TEK gerçek-tur sinyali olan TurnToken'dır —
                // sadece seçilen konuşmacı TurnToken alır, broadcast hedefleri ise düz
                // ChatMessage listesi alır (bkz. GroupChatHost.TakeTurnAsync/BroadcastAsync).
                if (invoked.Data is not TurnToken) return null;

                st.IterationCount++;

                var sig = $"{executorId}:running";
                if (sig == st.LastAgentSignature) return null;
                st.LastAgentSignature = sig;

                // Tool çağrıları (ör. UI hint emisyonu) bu ajan adına etiketlensin —
                // hangi ajanın hint ürettiğini stream event sırasına bağlı kalmadan bilelim.
                _approvalContext.SetCurrentAgent(executorId);

                // Visit'i sadece ActiveVisits'e kaydet; trace listesine CompletedEvent'te
                // ekleyeceğiz.
                st.ActiveVisits[executorId] = new AgentVisit
                {
                    AgentName = executorId,
                    StartedAt = DateTime.UtcNow
                };

                return (executorId, "running");
            }

            case ExecutorCompletedEvent completed:
            {
                var completedId = completed.ExecutorId ?? "unknown";
                if (WorkflowResponseExtractor.IsInternalWorkflowExecutor(completedId)) return null;

                // ActiveVisits'te kaydı yoksa bu, Invoked aşamasında TurnToken taşımadığı
                // için zaten atlanmış bir broadcast/senkron tamamlanmasıdır — yok say.
                if (!st.ActiveVisits.TryGetValue(completedId, out var visit)) return null;

                var sig = $"{completedId}:done";
                if (sig == st.LastAgentSignature) return null;
                st.LastAgentSignature = sig;

                visit.CompletedAt = DateTime.UtcNow;
                st.ActiveVisits.Remove(completedId);
                st.Trace.AgentVisits.Add(visit);
                _traceStore.Update(st.Trace);

                return (completedId, "done");
            }

            case WorkflowOutputEvent output:
            {
                st.Result = WorkflowResponseExtractor.ExtractResultFromOutput(output);
                var planning = WorkflowResponseExtractor.ExtractPlanningFromOutput(output);
                if (planning != null) st.Trace.Planning = planning;
                var specialistReasonings =
                    WorkflowResponseExtractor.ExtractSpecialistReasoningsFromOutput(output);
                if (specialistReasonings.Count > 0)
                    st.Trace.SpecialistReasonings.AddRange(specialistReasonings);
                if (planning != null || specialistReasonings.Count > 0)
                    _traceStore.Update(st.Trace);
                return null;
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// Workflow başarıyla tamamlandığında trace'i kapatır ve bağlı yan etkileri
    /// (eskalasyon işleme, episodik bellek, müşteri profili) tetikler.
    /// </summary>
    private async Task FinalizeTraceAsync(
        TraceState st,
        AgentSession? session,
        string query,
        string result,
        string terminationReason)
    {
        _approvalGate.ProcessPendingEscalations(st.Trace, query, result);
        PopulateAgentVisitOutputs(st.Trace, result);
        WriteEpisodicMemorySafe(st.Trace, query, result);
        await UpdateCustomerProfileSafeAsync(session, st.Trace, query, result);

        _traceStore.Complete(st.Trace.TraceId,
            terminationReason: terminationReason,
            finalResponse: result);
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

        var replanHint = ConsumeForceReplanHint(session);
        if (replanHint != null)
        {
            messages.Add(new ChatMessage(ChatRole.System, replanHint));
        }

        messages.Add(new ChatMessage(ChatRole.User, query));

        return messages;
    }

    /// <summary>
    /// ForceReplanNextTurn bayrağını atomik olarak okur ve temizler — tek kullanımlık (one-shot)
    /// olması gerektiği için birden fazla eşzamanlı çağrının (paralel alt-görevler veya aynı
    /// session'a gelen eşzamanlı istekler) aynı flag'i birden çok kez tüketmesini engeller.
    /// </summary>
    private static string? ConsumeForceReplanHint(AgentSession? session)
    {
        if (session is null) return null;

        lock (session)
        {
            if (!session.State.ForceReplanNextTurn) return null;

            var hint = WellKnown.FallbackMessages.ReplanPlanningHint;
            if (!string.IsNullOrWhiteSpace(session.State.ReplanNote))
            {
                hint += $"\n\n📌 Admin notu (sadece sana, müşteri görmez): \"{session.State.ReplanNote}\"";
            }

            session.State.ForceReplanNextTurn = false;
            session.State.ReplanNote = null;
            return hint;
        }
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

    private async Task<string> RewriteRoutingMessageAsync(
        string routingMessage, string originalQuery, CancellationToken ct)
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

            var response = await _chatClient.GetResponseAsync(prompt, cancellationToken: ct);
            return response.Text ?? WellKnown.FallbackMessages.RoutingRewrite;
        }
        catch (Exception ex)
        {
            // Sessizce yutmak, sürekli patlayan bir LLM çağrısını görünmez kılıyordu —
            // kullanıcı hep aynı fallback'i görür, sebebi hiçbir yere yazılmazdı.
            _loggerFactory.CreateLogger<CustomerSupportTeam>().LogWarning(
                ex, "Routing mesajı yeniden yazılamadı; fallback mesaj kullanılıyor.");
            return WellKnown.FallbackMessages.RoutingRewrite;
        }
    }

    private async Task<string> RunDecomposedAsync(
        string query,
        List<ConversationMessage>? conversationHistory,
        AgentSession? session,
        ReasoningResult reasoning,
        CancellationToken ct)
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
                    await sem.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        var subQuery = SubTaskOrchestrator.FormatSubTaskQuery(sub);
                        var subReasoning = SubTaskOrchestrator.CreateSubTaskReasoning(reasoning, sub);
                        var subResp = await RunAsync(subQuery, historySnapshot, session, subReasoning, ct)
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
                    var subResp = await RunAsync(subQuery, runningHistory, session, subReasoning, ct)
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
        var sessionId = session?.SessionId ?? string.Empty;
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
                        // Bu paralel dalın kendi (izole) async akışında ambient ajan adını
                        // sub.TargetAgent'a sabitle — RunAsync hint drain etmediği için
                        // (streaming değil), ürettiği ipuçları burada, kendi TargetAgent'ıyla
                        // etiketlenmiş biçimde kuyrukta bekler.
                        _approvalContext.SetCurrentAgent(sub.TargetAgent);
                        var resp = await RunAsync(subQuery, historySnapshot, session, subReasoning, ct)
                            .ConfigureAwait(false);
                        var hints = _uiHint.DrainPending(sessionId);
                        return (sub, resp, hints);
                    }
                    finally { sem.Release(); }
                }).ToList();

                var results = await Task.WhenAll(tasks).ConfigureAwait(false);

                foreach (var (sub, resp, hints) in results.OrderBy(t => t.sub.Order))
                {
                    collected[sub.Order] = SubTaskOrchestrator.FormatSubTaskResult(sub, resp);
                    runningHistory.Add(new ConversationMessage(ConversationRoles.User,
                        SubTaskOrchestrator.FormatSubTaskQuery(sub)));
                    runningHistory.Add(new ConversationMessage(ConversationRoles.Assistant, resp));

                    foreach (var hint in hints)
                        yield return hint;

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

                    _approvalContext.SetCurrentAgent(sub.TargetAgent);
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

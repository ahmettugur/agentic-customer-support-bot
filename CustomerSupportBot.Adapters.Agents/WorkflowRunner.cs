// Adapters.Agents/WorkflowRunner.cs
// Tek bir (decompose edilmemiş) kullanıcı sorgusu için GroupChat workflow koşusu:
// mesaj hazırlığı, workflow execution, trace toplama ve stream event üretimi.
// Compound query'lerin alt görevlere bölünmesi DecomposedRunner'ın sorumluluğundadır —
// o da her alt görev için bu sınıfın RunAsync/RunStreamingAsync'ini çağırır.

using System.Runtime.CompilerServices;
using System.Text;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;
using AgentSession = CustomerSupportBot.Domain.Model.AgentSession;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Agents;

internal sealed class WorkflowRunner
{
    private readonly AgentTeamFactory _factory;
    private readonly TurnFinalizer _finalizer;
    private readonly IContextPipeline _contextPipeline;
    private readonly IChatClient _chatClient;
    private readonly WorkflowGuardOptions _guards;
    private readonly IReasoningTraceStore _traceStore;
    private readonly IPromptRepository _prompts;
    private readonly ApprovalGateService _approvalGate;
    private readonly IUiHintEmitter _uiHint;
    private readonly IApprovalContextAccessor _approvalContext;
    private readonly ILoggerFactory _loggerFactory;

    public WorkflowRunner(
        AgentTeamFactory factory,
        TurnFinalizer finalizer,
        IContextPipeline contextPipeline,
        IChatClient chatClient,
        WorkflowGuardOptions guards,
        IReasoningTraceStore traceStore,
        IPromptRepository prompts,
        ApprovalGateService approvalGate,
        IUiHintEmitter uiHint,
        IApprovalContextAccessor approvalContext,
        ILoggerFactory loggerFactory)
    {
        _factory = factory;
        _finalizer = finalizer;
        _contextPipeline = contextPipeline;
        _chatClient = chatClient;
        _guards = guards;
        _traceStore = traceStore;
        _prompts = prompts;
        _approvalGate = approvalGate;
        _uiHint = uiHint;
        _approvalContext = approvalContext;
        _loggerFactory = loggerFactory;
    }

    public async Task<string> RunAsync(
        string query,
        List<ConversationMessage>? conversationHistory,
        AgentSession? session,
        ReasoningResult? reasoning,
        CancellationToken ct)
    {
        var messages = await BuildWorkflowMessagesAsync(query, conversationHistory, session, reasoning);

        using var timeoutCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(_guards.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var effectiveCt = linkedCts.Token;

        var st = StartTraceState(session, query, reasoning);

        var workflow = _factory.CreateWorkflow();
        await using var run = await InProcessExecution.RunStreamingAsync(workflow, messages, cancellationToken: effectiveCt);
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
            await StopRunGracefullyAsync(run);
            _approvalGate.ProcessPendingEscalations(st.Trace, query, "");
            _traceStore.Complete(st.Trace.TraceId,
                terminationReason: WellKnown.Termination.ReasonTimeout,
                error: $"Workflow {_guards.TimeoutSeconds}s timeout'a takıldı");
            throw ExceptionTranslator.Translate(
                new TimeoutException($"Workflow {_guards.TimeoutSeconds}s timeout'a takıldı."),
                "RunAsync workflow timeout.");
        }

        if (ct.IsCancellationRequested)
        {
            await StopRunGracefullyAsync(run);
            _traceStore.Complete(st.Trace.TraceId,
                terminationReason: "cancelled",
                error: "İstek çağıran tarafından iptal edildi.");
            ct.ThrowIfCancellationRequested();
        }

        if (workflowError != null)
        {
            _approvalGate.ProcessPendingEscalations(st.Trace, query, "");
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

        await _finalizer.FinalizeAsync(st.Trace, session, query, result, terminationReason);

        return result;
    }

    public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(
        string query,
        List<ConversationMessage>? conversationHistory,
        AgentSession? session,
        ReasoningResult? reasoning,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var sessionId = session?.SessionId ?? string.Empty;

        var messages = await BuildWorkflowMessagesAsync(query, conversationHistory, session, reasoning);

        using var timeoutCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(_guards.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var effectiveCt = linkedCts.Token;

        var st = StartTraceState(session, query, reasoning);

        var workflow = _factory.CreateWorkflow();
        await using var run = await InProcessExecution.RunStreamingAsync(workflow, messages, cancellationToken: effectiveCt);
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

            // Yalnızca gözlemlenebilir bir değişiklik varsa (ajan durumu, gerçek zamanlı
            // yanıt delta'sı) stream event yayınlanır.
            foreach (var surfaced in ApplyTraceEvent(st, evt))
                yield return surfaced;

            // Tool çağrıları sırasında biriken UI ipuçlarını (ör. category_picker) hemen yayınla
            foreach (var hint in _uiHint.DrainPending(sessionId))
                yield return hint;
        }

        st.Trace.IterationCount = st.IterationCount;

        if (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            await StopRunGracefullyAsync(run);
            _approvalGate.ProcessPendingEscalations(st.Trace, query, "");
            _traceStore.Complete(st.Trace.TraceId,
                terminationReason: WellKnown.Termination.ReasonTimeout,
                error: $"Workflow {_guards.TimeoutSeconds}s timeout'a takıldı");
            yield return new StreamEvent(StreamEventTypes.Error,
                new { message = $"İşlem {_guards.TimeoutSeconds} saniyede tamamlanamadı." });
            yield break;
        }

        if (ct.IsCancellationRequested)
        {
            // İstemci bağlantıyı kesti (durdur butonu, sekme kapatma, yeni sohbet) —
            // RunAsync'in (non-streaming) aynı durumdaki davranışıyla simetrik: workflow'a
            // kooperatif dur sinyali gönderilir, gereksiz finalize/persist adımları
            // (RewriteRoutingMessageAsync, TurnFinalizer) atlanır. Önceden bu dal burada
            // eksikti — linked token expire oluyor, enumeration sessizce bitiyordu ama
            // StopRunGracefullyAsync hiç çağrılmıyordu.
            await StopRunGracefullyAsync(run);
            _approvalGate.ProcessPendingEscalations(st.Trace, query, "");
            _traceStore.Complete(st.Trace.TraceId,
                terminationReason: "cancelled",
                error: "İstek çağıran tarafından iptal edildi.");
            yield break;
        }

        if (workflowError != null)
        {
            _approvalGate.ProcessPendingEscalations(st.Trace, query, "");
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

        await _finalizer.FinalizeAsync(st.Trace, session, query, result, terminationReason);

        // ResponseAgent'ın gerçek token akışı zaten ApplyTraceEvent içinde (AgentResponseUpdateEvent
        // dalı) yayınlandıysa response_start + delta'lar döngü sırasında gönderilmiş demektir —
        // burada tekrar başlatmak yerine sadece son (temizlenmiş, kanonik) metinle tamamlanır.
        // Akış herhangi bir sebeple (ör. framework'ün emitUpdateEvents desteklemediği bir yol,
        // ya da her şey TERMINATE öncesinde kesilecek kadar kısaydı) hiç tetiklenmediyse eski
        // yapay parçalama fallback olarak devrede kalır — böylece davranış hiçbir zaman geriye
        // gitmez.
        if (!st.ResponseStreamStarted)
        {
            yield return new StreamEvent(StreamEventTypes.ResponseStart,
                new { terminationReason });

            await foreach (var chunk in WorkflowResponseExtractor.StreamTextInChunksAsync(result, effectiveCt))
            {
                yield return new StreamEvent(StreamEventTypes.ResponseDelta, new TextDeltaPayload(chunk));
            }
        }

        yield return new StreamEvent(StreamEventTypes.ResponseComplete,
            new { text = result, terminationReason });
    }

    /// <summary>
    /// Tek bir workflow koşusunun trace toplama durumu.
    /// </summary>
    private sealed class TraceState
    {
        public required ReasoningTrace Trace { get; init; }
        public Dictionary<string, AgentVisit> ActiveVisits { get; } = new();
        public string? LastAgentSignature { get; set; }
        public int IterationCount { get; set; }
        public string Result { get; set; } = "";

        /// <summary>
        /// ResponseAgent'ın gerçek token akışından en az bir karakter yayınlandıysa true —
        /// bu turda <see cref="StreamEventTypes.ResponseStart"/> zaten gönderilmiş demektir,
        /// döngü sonrası kod tekrar göndermemeli ve StreamTextInChunksAsync yapay
        /// parçalamasına başvurmamalıdır.
        /// </summary>
        public bool ResponseStreamStarted { get; set; }
        public ResponseStreamFilter ResponseFilter { get; } = new();
    }

    /// <summary>
    /// ResponseAgent'ın ham token akışını kullanıcıya göndermeden önce "TERMINATE: reason=..."
    /// işaretinden (ve ondan sonra gelen self-critique JSON bloğundan, bkz. response-agent.md)
    /// arındırır. Chunk sınırları marker'ı bölebileceği için (ör. "...cevap TERM" + "INATE...")
    /// marker uzunluğu kadar güvenlik payı tutulur; yalnızca kesinlikle marker'a ait olmadığı
    /// bilinen kısım hemen yayınlanır.
    /// </summary>
    private sealed class ResponseStreamFilter
    {
        private const string Marker = "TERMINATE";
        private readonly StringBuilder _pending = new();
        private bool _cutoff;

        public string Feed(string chunk)
        {
            if (_cutoff || string.IsNullOrEmpty(chunk)) return "";

            _pending.Append(chunk);
            var text = _pending.ToString();

            var idx = text.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                _cutoff = true;
                var safe = text[..idx];
                _pending.Clear();
                return safe;
            }

            var emitLen = Math.Max(0, text.Length - (Marker.Length - 1));
            if (emitLen == 0) return "";

            var toEmit = text[..emitLen];
            _pending.Remove(0, emitLen);
            return toEmit;
        }
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

    private static readonly List<StreamEvent> NoEvents = new();

    /// <summary>
    /// Bir workflow event'inin trace yan etkilerini uygular ve varsa bu event'ten
    /// kaynaklanan (boş olabilir) stream event listesini döner. Çağıran taraf streaming
    /// değilse (RunAsync) dönüş değerini yok sayabilir.
    /// </summary>
    private List<StreamEvent> ApplyTraceEvent(TraceState st, WorkflowEvent evt)
    {
        switch (evt)
        {
            case ExecutorInvokedEvent invoked:
            {
                var executorId = invoked.ExecutorId ?? "unknown";
                if (WorkflowResponseExtractor.IsInternalWorkflowExecutor(executorId)) return NoEvents;

                // GroupChatHost her turda seçilmeyen tüm ajanlara da geçmişlerini senkron
                // tutmak için mesaj yollar (BroadcastAsync) — bu da Invoked/Completed
                // event çiftini tetikler ama ajan gerçekte çalışmaz. Süreye bakarak ayırt
                // etmek güvenilir değil (gerçek ajanlar da bazen <1ms'de tamamlanabiliyor);
                // asıl ayırt edici framework'ün TEK gerçek-tur sinyali olan TurnToken'dır —
                // sadece seçilen konuşmacı TurnToken alır, broadcast hedefleri ise düz
                // ChatMessage listesi alır (bkz. GroupChatHost.TakeTurnAsync/BroadcastAsync).
                if (invoked.Data is not TurnToken) return NoEvents;

                st.IterationCount++;

                var sig = $"{executorId}:running";
                if (sig == st.LastAgentSignature) return NoEvents;
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

                return [new StreamEvent(StreamEventTypes.Agent, new { name = executorId, status = "running" })];
            }

            case ExecutorCompletedEvent completed:
            {
                var completedId = completed.ExecutorId ?? "unknown";
                if (WorkflowResponseExtractor.IsInternalWorkflowExecutor(completedId)) return NoEvents;

                // ActiveVisits'te kaydı yoksa bu, Invoked aşamasında TurnToken taşımadığı
                // için zaten atlanmış bir broadcast/senkron tamamlanmasıdır — yok say.
                if (!st.ActiveVisits.TryGetValue(completedId, out var visit)) return NoEvents;

                var sig = $"{completedId}:done";
                if (sig == st.LastAgentSignature) return NoEvents;
                st.LastAgentSignature = sig;

                visit.CompletedAt = DateTime.UtcNow;
                st.ActiveVisits.Remove(completedId);
                st.Trace.AgentVisits.Add(visit);

                // Planning/specialist reasoning'i her gerçek turda ara-durumdan da çıkar
                // (WorkflowOutputEvent'i beklemeden) — böylece timeout/hata ile workflow
                // hiç tamamlanmasa bile o ana kadar toplanan reflection'lar (ör.
                // needs_escalation) trace'e işlenmiş olur ve eskalasyon tetiklenebilir.
                if (completed.Data is IEnumerable<ChatMessage> pendingMessages)
                {
                    var planning = WorkflowResponseExtractor.ExtractPlanning(pendingMessages);
                    if (planning != null) st.Trace.Planning = planning;

                    var reasonings = WorkflowResponseExtractor.ExtractSpecialistReasonings(pendingMessages);

                    if (completedId.StartsWith(WellKnown.AgentNames.HumanHandoff, StringComparison.OrdinalIgnoreCase))
                        EnsureHumanHandoffEscalation(pendingMessages, reasonings);

                    if (reasonings.Count > 0) MergeSpecialistReasonings(st.Trace, reasonings);
                }

                _traceStore.Update(st.Trace);

                return [new StreamEvent(StreamEventTypes.Agent, new { name = completedId, status = "done" })];
            }

            // AgentResponseUpdateEvent, WorkflowOutputEvent'ten türer — daha spesifik olduğu
            // için switch'te ondan ÖNCE kontrol edilmeli. TurnToken(emitEvents:true) her
            // ajan turunda gerçek zamanlı LLM token delta'larını bu event tipiyle yayınlar
            // (bkz. AIAgentHostExecutor.InvokeAgentAsync). Sadece ResponseAgent'ın delta'ları
            // kullanıcıya gösterilir — diğer ajanların (Planning/Order/Complaint/HumanHandoff)
            // ham çıktısı yapılandırılmış JSON reasoning'dir, kullanıcıya asla akıtılmamalı.
            case AgentResponseUpdateEvent updateEvt:
            {
                var executorId = updateEvt.ExecutorId ?? "";
                if (!executorId.StartsWith(WellKnown.AgentNames.Response, StringComparison.OrdinalIgnoreCase))
                    return NoEvents;

                var chunk = updateEvt.Update.Text;
                if (string.IsNullOrEmpty(chunk)) return NoEvents;

                var safeText = st.ResponseFilter.Feed(chunk);
                if (safeText.Length == 0) return NoEvents;

                var results = new List<StreamEvent>();
                if (!st.ResponseStreamStarted)
                {
                    st.ResponseStreamStarted = true;
                    results.Add(new StreamEvent(StreamEventTypes.ResponseStart, new { terminationReason = (string?)null }));
                }
                results.Add(new StreamEvent(StreamEventTypes.ResponseDelta, new TextDeltaPayload(safeText)));
                return results;
            }

            case WorkflowOutputEvent output:
            {
                st.Result = WorkflowResponseExtractor.ExtractResultFromOutput(output);
                var planning = WorkflowResponseExtractor.ExtractPlanningFromOutput(output);
                if (planning != null) st.Trace.Planning = planning;
                var specialistReasonings =
                    WorkflowResponseExtractor.ExtractSpecialistReasoningsFromOutput(output);

                // Son güvence: turun tamamı burada görünür durumda — ExecutorCompletedEvent
                // aşamasında bir sebeple (event kaçırma, sıralama) yakalanamamışsa bile
                // human_handoff_tool çağrısı burada da kontrol edilir.
                if (output.Data is IEnumerable<ChatMessage> allMessages)
                    EnsureHumanHandoffEscalation(allMessages, specialistReasonings);

                if (specialistReasonings.Count > 0)
                    MergeSpecialistReasonings(st.Trace, specialistReasonings);
                if (planning != null || specialistReasonings.Count > 0)
                    _traceStore.Update(st.Trace);
                return NoEvents;
            }

            default:
                return NoEvents;
        }
    }

    /// <summary>
    /// Ajan başına en güncel reasoning'i tutarak birleştirir — aynı ajan hem ara-durumda
    /// (ExecutorCompletedEvent) hem final WorkflowOutputEvent'te görülebildiği için dedup
    /// gerekir; aksi halde eskalasyon adayı listesi mükerrer kayıt üretir.
    /// </summary>
    private static void MergeSpecialistReasonings(ReasoningTrace trace, List<SpecialistReasoning> incoming)
    {
        foreach (var r in incoming)
        {
            var idx = trace.SpecialistReasonings.FindIndex(x =>
                string.Equals(x.AgentName, r.AgentName, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) trace.SpecialistReasonings[idx] = r;
            else trace.SpecialistReasonings.Add(r);
        }
    }

    private const string HandoffFallbackSummary =
        "Kullanıcı insan temsilciyle görüşme talep etti (human_handoff_tool çağrıldı).";

    private const string HandoffFallbackReason =
        "human_handoff_tool çağrıldı ama LLM reflection'ı needs_escalation olarak " +
        "işaretlemedi; sistem garantisiyle düzeltildi.";

    /// <summary>
    /// Kullanıcı açıkça insan temsilci istediğinde (human_handoff_tool çağrıldığında)
    /// eskalasyonun oluşmasını KOD İLE garanti eder — LLM'in postToolReflection'da
    /// <c>status=needs_escalation</c> yazmayı unutmasına/yanlış yazmasına bağlı kalmaz.
    /// EscalationPolicyService.ProcessPendingEscalations SADECE bu status'e bakarak
    /// eskalasyon açtığı için, model reflection JSON'unu yanlış üretirse kullanıcı
    /// "temsilci bağlanacak" mesajı alır ama admin panelinde hiçbir kayıt oluşmazdı —
    /// sessiz bir başarısızlık noktasıydı.
    ///
    /// <para>
    /// Bilinçli takas: garanti TEK YÖNLÜ. Tool çağrısı da bir LLM kararı olduğu için, model
    /// handoff tool'unu gereksiz çağırıp reflection'da "aslında gerek yok" dese bile kayıt
    /// açılır. Müşteri desteğinde kaçırılan eskalasyonun maliyeti fazladan eskalasyondan
    /// yüksek olduğu ve admin panelinde dismiss yolu bulunduğu için bu yön tercih edildi.
    /// Panelde gürültü artarsa bakılacak ilk yer burasıdır.
    /// </para>
    ///
    /// <para>
    /// Bilinen kapsam dışı köşe: HumanHandoffAgent turu uçuş hâlindeyken workflow timeout'a
    /// takılırsa tool çağrısı hiç tamamlanmadığı için FunctionCallContent oluşmaz ve garanti
    /// devreye giremez.
    /// </para>
    /// </summary>
    private static void EnsureHumanHandoffEscalation(
        IEnumerable<ChatMessage> messages, List<SpecialistReasoning> reasonings)
    {
        if (!WorkflowResponseExtractor.ContainsHumanHandoffToolCall(messages)) return;

        var existing = reasonings.FirstOrDefault(r =>
            string.Equals(r.AgentName, WellKnown.AgentNames.HumanHandoff, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            existing = new SpecialistReasoning { AgentName = WellKnown.AgentNames.HumanHandoff };
            reasonings.Add(existing);
        }

        var reflection = existing.PostToolReflection ??= new PostToolReflection();

        // LLM zaten doğru işaretlemiş — hiçbir alanına dokunma.
        if (reflection.StatusEnum == TaskCompletionStatus.NeedsEscalation) return;

        // Yerinde düzelt (remove+replace DEĞİL): PreToolCheck, ResultConfidence, ResultNotes
        // ve MissingContext gibi LLM'in ürettiği diğer alanlar korunur. MissingContext ayrıca
        // fonksiyonel — EscalationPolicyService bunu doğrudan EscalationRequest'e kopyalıyor,
        // dolayısıyla kaybı admin'in gördüğü kayıttan "hangi bilgi eksikti"yi silerdi.
        reflection.Status = WellKnown.TaskStatuses.NeedsEscalation;
        reflection.TaskComplete = false;
        if (string.IsNullOrWhiteSpace(reflection.Summary)) reflection.Summary = HandoffFallbackSummary;
        if (string.IsNullOrWhiteSpace(reflection.HandoffReason)) reflection.HandoffReason = HandoffFallbackReason;
    }

    /// <summary>
    /// Timeout/iptal anında koşuyu kooperatif olarak durdurmayı dener — mevcut superstep'ten
    /// sonra step-runner'ın durmasını ister. Şu anki MAF sürümünde (1.15.0) bu, hâlihazırda
    /// devam eden bir LLM HTTP çağrısını anında kesmez (framework sınırlaması), ama bir
    /// sonraki ajan turunun başlamasını engeller — CancellationToken.None ile bırakmaktan
    /// daha iyi. Hata olursa yutulur; bu zaten en iyi çaba (best-effort) bir temizliktir.
    /// </summary>
    private async Task StopRunGracefullyAsync(StreamingRun run)
    {
        try
        {
            await run.CancelRunAsync();
        }
        catch (Exception ex)
        {
            _loggerFactory.CreateLogger<WorkflowRunner>()
                .LogDebug(ex, "Workflow run graceful stop sırasında hata (yok sayıldı)");
        }
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
            _loggerFactory.CreateLogger<WorkflowRunner>().LogWarning(
                ex, "Routing mesajı yeniden yazılamadı; fallback mesaj kullanılıyor.");
            return WellKnown.FallbackMessages.RoutingRewrite;
        }
    }

    private static ChatRole ToChatRole(string role) => role switch
    {
        ConversationRoles.User   => ChatRole.User,
        ConversationRoles.System => ChatRole.System,
        _                        => ChatRole.Assistant
    };
}

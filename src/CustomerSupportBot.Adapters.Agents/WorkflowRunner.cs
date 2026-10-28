// Adapters.Agents/WorkflowRunner.cs
// Tek bir (decompose edilmemiş) kullanıcı sorgusu için GroupChat workflow koşusu:
// workflow execution + event loop orkestrasyonu. Mesaj hazırlığı WorkflowMessageBuilder'a,
// trace/event işleme WorkflowTraceEventProcessor'a taşındı (#47 — bölme) — bu sınıf artık
// yalnızca "koşuyu başlat, event'leri dinle, anormal sonlanmayı yönet" sorumluluğunu taşır.
// Compound query'lerin alt görevlere bölünmesi DecomposedRunner'ın sorumluluğundadır —
// o da her alt görev için bu sınıfın RunAsync/RunStreamingAsync'ini çağırır.

using System.Runtime.CompilerServices;
using System.Diagnostics;
using CustomerSupportBot.Adapters.Agents.Checkpointing;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Services.Approval;
using CustomerSupportBot.Application.Services.Reasoning;
using Microsoft.Extensions.Options;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;
using AgentSession = CustomerSupportBot.Domain.Model.AgentSession;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Agents;

internal sealed class WorkflowRunner : IWorkflowRunner
{
    private readonly AgentTeamFactory _factory;
    private readonly TurnFinalizer _finalizer;
    private readonly WorkflowGuardOptions _guards;
    private readonly IReasoningTraceStore _traceStore;
    private readonly ApprovalGateService _approvalGate;
    private readonly IUiHintEmitter _uiHint;
    private readonly ILoggerFactory _loggerFactory;
    private readonly WorkflowTraceEventProcessor _traceProcessor;
    private readonly WorkflowMessageBuilder _messageBuilder;
    private readonly WorkflowCheckpointing? _checkpointing;
    private readonly IApprovalContextAccessor? _approvalContext;
    private readonly IOptionsMonitor<AgentRoutingOptions>? _routing;

    public WorkflowRunner(
        AgentTeamFactory factory,
        TurnFinalizer finalizer,
        WorkflowGuardOptions guards,
        IReasoningTraceStore traceStore,
        ApprovalGateService approvalGate,
        IUiHintEmitter uiHint,
        ILoggerFactory loggerFactory,
        WorkflowTraceEventProcessor traceProcessor,
        WorkflowMessageBuilder messageBuilder,
        WorkflowCheckpointing? checkpointing = null,
        IApprovalContextAccessor? approvalContext = null,
        IOptionsMonitor<AgentRoutingOptions>? routing = null)
    {
        _routing = routing;
        _factory = factory;
        _finalizer = finalizer;
        _guards = guards;
        _traceStore = traceStore;
        _approvalGate = approvalGate;
        _uiHint = uiHint;
        _loggerFactory = loggerFactory;
        _traceProcessor = traceProcessor;
        _messageBuilder = messageBuilder;
        _checkpointing = checkpointing;
        _approvalContext = approvalContext;
    }

    /// <summary>Bkz. <see cref="IAgentTeamPort.GetWorkflowDiagram"/>.</summary>
    public string GetWorkflowDiagram() => _factory.CreateWorkflow().ToMermaidString();

    public async Task<string> RunAsync(
        string query,
        List<ConversationMessage>? conversationHistory,
        AgentSession? session,
        ReasoningResult? reasoning,
        CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(_guards.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var effectiveCt = linkedCts.Token;

        WorkflowPrompt prompt;
        try
        {
            prompt = await _messageBuilder.BuildWorkflowMessagesAsync(
                query, conversationHistory, session, reasoning, effectiveCt);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw ExceptionTranslator.Translate(
                new TimeoutException($"Workflow {_guards.TimeoutSeconds}s timeout'a takıldı."),
                "RunAsync workflow mesaj hazırlığı timeout.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Eskiden burada genel bir catch yoktu — yalnızca timeout kaynaklı
            // OperationCanceledException yakalanıyordu. BuildWorkflowMessagesAsync'ten gelen
            // başka bir hata (ör. beklenmeyen bir null-ref) bu metottan ham olarak sızıyordu;
            // ExceptionTranslator hiç devreye girmiyordu (bkz. RunStreamingAsync'teki simetrik
            // düzeltme, bulgu 1.3).
            throw ExceptionTranslator.Translate(ex, "RunAsync workflow mesaj hazırlığı hatası.");
        }
        var messages = prompt.Messages;

        var st = await _traceProcessor.StartTraceStateAsync(session, query, reasoning);
        _traceProcessor.BindToAmbientContext(st);
        st.Trace.EstimatedTokens = TokenEstimator.Estimate(messages.Select(m => m.Text));
        st.Trace.ContextParts = ToContextUsage(prompt.Context);

        var workflow = _factory.CreateWorkflow(reasoning?.ConstrainedTargetAgent, ResolveFirstAgent(reasoning, st));
        var checkpoint = await BeginCheckpointingAsync(session, conversationHistory, reasoning, st, query);
        await using var turnGuard = checkpoint is null ? null : new CheckpointTurnGuard(checkpoint, st.Trace.TraceId, ct);
        var runTimer = Stopwatch.StartNew();
        await using var run = await StartRunAsync(workflow, messages, checkpoint, st.Trace.TraceId, effectiveCt);
        var started = await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        return await DriveToResultAsync(run, started, st, query, session, timeoutCts, ct, effectiveCt, runTimer);
    }

    /// <summary>
    /// Koşunun event döngüsünü sürer, anormal sonlanmayı ele alır, nihai yanıtı üretir ve turu kalıcılaştırır.
    /// <see cref="RunAsync"/> ve checkpoint'ten devam (<see cref="ResumeAsync"/>) AYNI yolu kullanır — bu blok iki
    /// yerde kopya olsaydı, daha önce streaming/non-streaming arasında olduğu gibi sessizce sapardı.
    /// </summary>
    private async Task<string> DriveToResultAsync(
        StreamingRun run,
        bool started,
        WorkflowTraceEventProcessor.TraceState st,
        string query,
        AgentSession? session,
        CancellationTokenSource timeoutCts,
        CancellationToken ct,
        CancellationToken effectiveCt,
        Stopwatch runTimer)
    {
        Exception? workflowError = null;

        if (!started)
        {
            // Eskiden bu dönüş değeri hiç kontrol edilmiyordu (bkz. bulgu 2.2). false dönerse
            // workflow HİÇ başlamaz; event döngüsü boş biter, st.Result "" kalır ve finalizer
            // boş bir yanıtı kalıcılaştırırdı — kullanıcı sessizce boş bir response_complete
            // alırdı. Artık bu durum, olay döngüsüne hiç girmeden diğer hata yollarıyla AYNI
            // (FinalizeAbnormalTerminationAsync → trace kapatma → ExceptionTranslator) yoldan
            // ele alınır.
            workflowError = new InvalidOperationException(
                "Workflow turu başlatılamadı — TrySendMessageAsync false döndü.");
        }
        else
        {
            await foreach (var (evt, evtError) in EnumerateWorkflowEventsSafely(run, effectiveCt))
            {
                if (evtError != null)
                {
                    workflowError = evtError;
                    break;
                }
                if (evt == null) continue;

                if (evt is RequestInfoEvent requestInfo)
                {
                    await HandleRequestInfoEventAsync(run, requestInfo, st, effectiveCt);
                    continue;
                }

                if (evt is WorkflowErrorEvent errorEvt)
                {
                    workflowError = errorEvt.Exception ?? new InvalidOperationException("workflow error");
                    break;
                }

                _traceProcessor.ApplyTraceEvent(st, evt);
            }
        }

        var outcome = await FinalizeAbnormalTerminationAsync(run, st, query, timeoutCts, ct, workflowError);
        WorkflowMetrics.Record(outcome.Kind switch
        {
            RunOutcomeKind.Completed => "ok",
            RunOutcomeKind.TimedOut => "timeout",
            RunOutcomeKind.Cancelled => "cancelled",
            _ => "error"
        }, runTimer.Elapsed);
        switch (outcome.Kind)
        {
            case RunOutcomeKind.TimedOut:
                throw ExceptionTranslator.Translate(
                    new TimeoutException($"Workflow {_guards.TimeoutSeconds}s timeout'a takıldı."),
                    "RunAsync workflow timeout.");
            case RunOutcomeKind.Cancelled:
                ct.ThrowIfCancellationRequested();
                break;
            case RunOutcomeKind.Error:
                throw ExceptionTranslator.Translate(
                    outcome.Error ?? new InvalidOperationException("workflow error"),
                    "RunAsync workflow hatası.");
        }

        try
        {
            var (result, _) = await BuildFinalResultAsync(st, session, query, effectiveCt);
            return result;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            await _traceStore.CompleteAsync(st.Trace.TraceId,
                terminationReason: WellKnown.Termination.ReasonTimeout,
                error: $"Workflow {_guards.TimeoutSeconds}s timeout'a takıldı during finalization");
            throw ExceptionTranslator.Translate(
                new TimeoutException($"Workflow {_guards.TimeoutSeconds}s timeout'a takıldı."),
                "RunAsync workflow finalization timeout.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Eskiden burada yalnızca timeout kaynaklı OperationCanceledException yakalanıyordu.
            // BuildFinalResultAsync'ten (TurnFinalizer.FinalizeAsync, RewriteRoutingMessageAsync
            // vb.) gelen başka bir hata (ör. DB yazma hatası) ham olarak dışarı sızıyordu VE
            // trace hiç kapatılmıyordu — StartTraceState ile açılan kayıt "başlatılmış ama
            // kapatılmamış" kalıyordu (bkz. bulgu 1.2). RunStreamingAsync'in eşdeğer
            // finalizationError bloğuyla aynı davranışa hizalandı: trace kapatılır, çağırana
            // ExceptionTranslator'dan geçmiş güvenli bir mesaj fırlatılır. Çağıranın kendi
            // iptali (ct.IsCancellationRequested, yukarıdaki timeout dalına girmeyen) bu dala
            // hiç girmez — streaming'deki simetrik davranışla tutarlı olarak ham
            // OperationCanceledException olarak yukarı yayılmaya devam eder.
            await _traceStore.CompleteAsync(st.Trace.TraceId,
                terminationReason: WellKnown.Termination.ReasonError,
                error: ex.Message);
            throw ExceptionTranslator.Translate(ex, "RunAsync workflow finalizasyon hatası.");
        }
    }

    public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(
        string query,
        List<ConversationMessage>? conversationHistory,
        AgentSession? session,
        ReasoningResult? reasoning,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var sessionId = session?.SessionId ?? string.Empty;

        using var timeoutCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(_guards.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var effectiveCt = linkedCts.Token;

        WorkflowPrompt? prompt = null;
        Exception? promptError = null;
        try
        {
            prompt = await _messageBuilder.BuildWorkflowMessagesAsync(
                query, conversationHistory, session, reasoning, effectiveCt);
        }
        catch (Exception ex)
        {
            promptError = ex;
        }

        if (promptError != null)
        {
            if (ct.IsCancellationRequested) yield break;
            // İstemciye ham promptError.Message DEĞİL, ExceptionTranslator'dan geçmiş güvenli
            // metin gönderilir — framework/HTTP iç detayları sızmasın diye (bkz. bulgu 1.3).
            var message = timeoutCts.IsCancellationRequested
                ? $"İşlem {_guards.TimeoutSeconds} saniyede tamamlanamadı."
                : ExceptionTranslator.Translate(promptError, "RunStreamingAsync mesaj hazırlığı hatası.").Message;
            yield return new StreamEvent(StreamEventTypes.Error, new { message });
            yield break;
        }

        var messages = prompt!.Messages;

        var st = await _traceProcessor.StartTraceStateAsync(session, query, reasoning);
        _traceProcessor.BindToAmbientContext(st);
        st.Trace.EstimatedTokens = TokenEstimator.Estimate(messages.Select(m => m.Text));
        st.Trace.ContextParts = ToContextUsage(prompt.Context);

        var workflow = _factory.CreateWorkflow(reasoning?.ConstrainedTargetAgent, ResolveFirstAgent(reasoning, st));
        var checkpoint = await BeginCheckpointingAsync(session, conversationHistory, reasoning, st, query);
        await using var turnGuard = checkpoint is null ? null : new CheckpointTurnGuard(checkpoint, st.Trace.TraceId, ct);
        var runTimer = Stopwatch.StartNew();
        await using var run = await StartRunAsync(workflow, messages, checkpoint, st.Trace.TraceId, effectiveCt);
        var started = await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        Exception? workflowError = null;

        if (!started)
        {
            // Bkz. RunAsync'teki aynı düzeltme (bulgu 2.2) — dönüş değeri eskiden kontrol
            // edilmiyordu, workflow hiç başlamadan sessizce boş bir yanıt üretilebiliyordu.
            workflowError = new InvalidOperationException(
                "Workflow turu başlatılamadı — TrySendMessageAsync false döndü.");
        }
        else
        {
            await foreach (var (evt, evtError) in EnumerateWorkflowEventsSafely(run, effectiveCt))
            {
                if (evtError != null)
                {
                    workflowError = evtError;
                    break;
                }
                if (evt == null) continue;

                if (evt is RequestInfoEvent requestInfo)
                {
                    await HandleRequestInfoEventAsync(run, requestInfo, st, effectiveCt);
                    continue;
                }

                if (evt is WorkflowErrorEvent errorEvt)
                {
                    workflowError = errorEvt.Exception ?? new InvalidOperationException("workflow error");
                    break;
                }

                // Yalnızca gözlemlenebilir bir değişiklik varsa (ajan durumu, gerçek zamanlı
                // yanıt delta'sı) stream event yayınlanır.
                foreach (var surfaced in _traceProcessor.ApplyTraceEvent(st, evt))
                    yield return surfaced;

                // Tool çağrıları sırasında biriken UI ipuçlarını (ör. category_picker) hemen yayınla
                foreach (var hint in _uiHint.DrainPending(sessionId))
                    yield return hint;
            }
        }

        var outcome = await FinalizeAbnormalTerminationAsync(run, st, query, timeoutCts, ct, workflowError);
        WorkflowMetrics.Record(outcome.Kind switch
        {
            RunOutcomeKind.Completed => "ok",
            RunOutcomeKind.TimedOut => "timeout",
            RunOutcomeKind.Cancelled => "cancelled",
            _ => "error"
        }, runTimer.Elapsed);
        switch (outcome.Kind)
        {
            case RunOutcomeKind.TimedOut:
                yield return new StreamEvent(StreamEventTypes.Error,
                    new { message = $"İşlem {_guards.TimeoutSeconds} saniyede tamamlanamadı." });
                yield break;
            case RunOutcomeKind.Cancelled:
                // İstemci bağlantıyı kesti (durdur butonu, sekme kapatma, yeni sohbet) —
                // workflow'a kooperatif dur sinyali gönderildi, gereksiz finalize/persist
                // adımları (RewriteRoutingMessageAsync, TurnFinalizer) atlanır.
                yield break;
            case RunOutcomeKind.Error:
                // Ham outcome.Error.Message DEĞİL — ExceptionTranslator'dan geçmiş güvenli
                // metin (bkz. bulgu 1.3). Ham detay zaten trace'e yazıldı
                // (FinalizeAbnormalTerminationAsync), admin panelinden görülebilir.
                yield return new StreamEvent(StreamEventTypes.Error,
                    new { message = ExceptionTranslator.Translate(
                        outcome.Error ?? new InvalidOperationException("workflow error"),
                        "RunStreamingAsync workflow hatası.").Message });
                yield break;
        }

        (string Result, string TerminationReason)? finalResult = null;
        Exception? finalizationError = null;
        try
        {
            finalResult = await BuildFinalResultAsync(st, session, query, effectiveCt);
        }
        catch (Exception ex)
        {
            finalizationError = ex;
        }

        if (finalizationError != null)
        {
            if (ct.IsCancellationRequested) yield break;
            var timedOut = timeoutCts.IsCancellationRequested;
            // Trace'e ham detay yazılır (admin/debug), istemciye sanitize edilmiş metin
            // gönderilir — bkz. bulgu 1.3.
            var traceMessage = timedOut
                ? $"İşlem {_guards.TimeoutSeconds} saniyede tamamlanamadı."
                : finalizationError.Message;
            var clientMessage = timedOut
                ? traceMessage
                : ExceptionTranslator.Translate(
                    finalizationError, "RunStreamingAsync finalizasyon hatası.").Message;
            await _traceStore.CompleteAsync(st.Trace.TraceId,
                terminationReason: timedOut
                    ? WellKnown.Termination.ReasonTimeout
                    : WellKnown.Termination.ReasonError,
                error: traceMessage);
            yield return new StreamEvent(StreamEventTypes.Error, new { message = clientMessage });
            yield break;
        }

        var (result, terminationReason) = finalResult!.Value;

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

            // İptal edilebilir DEĞİL ve bu kasıtlı: metin bu noktada zaten hesaplandı ve
            // BuildFinalResultAsync içinde kalıcılaştırıldı. Burada turun token'ına uymak,
            // yalnızca response_complete'in gönderilmemesine ve kullanıcının ekranında yarım
            // metin kalmasına yol açardı (bkz. SplitIntoDeltaChunks XML dokümanı).
            foreach (var chunk in WorkflowResponseExtractor.SplitIntoDeltaChunks(result))
            {
                yield return new StreamEvent(StreamEventTypes.ResponseDelta, new TextDeltaPayload(chunk));
            }
        }
        else
        {
            // Emit an incomplete protocol prefix as ordinary text when the stream ends.
            // Flush is a no-op if a complete termination prefix was already consumed.
            var trailing = st.ResponseFilter.Flush();
            if (!string.IsNullOrEmpty(trailing))
            {
                yield return new StreamEvent(StreamEventTypes.ResponseDelta, new TextDeltaPayload(trailing));
            }
        }

        // Tipli payload: bu metin turun KANONİK yanıtıdır ve delta akışından farklı olabilir
        // (delta'lar ham, bu metin temizlenmiş/yeniden yazılmış). ChatPortService
        // kalıcılaştırma için bunu okur — bkz. ResponseCompletePayload.
        yield return new StreamEvent(StreamEventTypes.ResponseComplete,
            new ResponseCompletePayload(result, terminationReason));
    }

    private async Task<WorkflowCheckpointing?> BeginCheckpointingAsync(
        AgentSession? session, List<ConversationMessage>? history, ReasoningResult? reasoning,
        WorkflowTraceEventProcessor.TraceState st, string query)
    {
        if (_checkpointing is null || !_checkpointing.ShouldCheckpoint(session, history, reasoning)) return null;
        return await _checkpointing.TryBeginAsync(st.Trace, query, session!, history!.Count, reasoning) ? _checkpointing : null;
    }

    private static ValueTask<StreamingRun> StartRunAsync(
        Workflow workflow, List<ChatMessage> messages, WorkflowCheckpointing? checkpoint, string traceId, CancellationToken ct) =>
        checkpoint is null
            ? InProcessExecution.RunStreamingAsync(workflow, messages, cancellationToken: ct)
            : InProcessExecution.RunStreamingAsync(workflow, messages, checkpoint.Manager, traceId, ct);

    /// <summary>
    /// Tur ne şekilde çıkarsa çıksın (dönüş, istisna, iterator'ın bırakılması) checkpoint'leri bırakır ya da saklar.
    /// <c>await using</c> ile kullanılır: RunStreamingAsync'in onlarca <c>yield break</c> çıkışına dokunmadan hepsini kapsar.
    /// </summary>
    private sealed class CheckpointTurnGuard(WorkflowCheckpointing checkpointing, string traceId, CancellationToken turnToken)
        : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => new(checkpointing.EndAsync(traceId, turnToken));
    }

    /// <summary>
    /// Çökme/kapanış yüzünden yarım kalan turu son checkpoint'inden sürdürür ve nihai yanıtı döner (kalıcılaştırma
    /// <see cref="BuildFinalResultAsync"/> içinde yapılır; konuşma geçmişine yazmak çağıranın işidir).
    /// </summary>
    public async Task<string> ResumeAsync(
        WorkflowTurnContext turn, AgentSession? session, CheckpointInfo from, CancellationToken ct)
    {
        if (_checkpointing is null)
            throw new InvalidOperationException("Checkpoint'leme yapılandırılmamış.");

        var trace = _traceStore.Get(turn.TraceId)
            ?? throw new InvalidOperationException($"Trace bulunamadı: {turn.TraceId}");

        // Başlangıçta "process_restart" olarak kapatılmış olabilir; tur yeniden açılır, bitince gerçek sonuçla kapanır.
        // Çökmede yalnızca iskelet kalıcıdır (ajan ziyaretleri tur bitince yazılır), bu yüzden yalnızca devam sonrası kısım
        // kaydedilir.
        trace.CompletedAt = null;
        trace.TerminationReason = null;
        trace.Error = null;
        trace.Reasoning = turn.Reasoning;
        _traceStore.Update(trace);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_guards.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var effectiveCt = linkedCts.Token;

        var st = _traceProcessor.AttachTraceState(trace);
        using var scope = _approvalContext?.SetScope(turn.SessionId, null, turn.Query, turn.CustomerId);
        _traceProcessor.BindToAmbientContext(st);

        var workflow = _factory.CreateWorkflow(null);
        var runTimer = Stopwatch.StartNew();
        await using var run = await InProcessExecution.ResumeStreamingAsync(workflow, from, _checkpointing.Manager, effectiveCt);
        return await DriveToResultAsync(run, started: true, st, turn.Query, session, timeoutCts, ct, effectiveCt, runTimer);
    }

    /// <summary>
    /// Koşu <b>normal</b> bittikten sonra ham workflow çıktısını kullanıcıya gösterilecek nihai
    /// metne çevirir ve turu kalıcılaştırır. Anormal sonlanmanın karşılığı için bkz.
    /// <see cref="FinalizeAbnormalTerminationAsync"/>.
    ///
    /// <para>
    /// Bu blok da <see cref="RunAsync"/>/<see cref="RunStreamingAsync"/> arasında birebir
    /// kopyaydı ve — <c>FinalizeAbnormalTerminationAsync</c>'te olduğu gibi — <b>sessizce
    /// sapmıştı</b>: routing yeniden yazımına non-streaming dalı çağıranın token'ını (<c>ct</c>),
    /// streaming dalı ise timeout'a bağlı token'ı (<c>effectiveCt</c>) veriyordu. Yani 60sn'lik
    /// bütçenin 55'i workflow'da geçtiyse rewrite streaming'de 5 saniyeye sıkışıyor,
    /// non-streaming'de sınırsız sürebiliyordu. Tek çağrı noktasına indirilerek bu fark yapısal
    /// olarak imkânsız hale getirildi; her iki çağıran da turun bütçesine bağlı token'ı geçer.
    /// </para>
    /// </summary>
    /// <summary>
    /// İlk ajanı belirler. <see cref="AgentRoutingMode.Planner"/>: <c>null</c> (Planning). <see cref="AgentRoutingMode.Shadow"/>: yine <c>null</c>
    /// (davranış değişmez) ama yönlendiricinin kararı turun sonunda Planning'inkiyle karşılaştırılmak üzere saklanır.
    /// <see cref="AgentRoutingMode.Deterministic"/>: yönlendirici karar verdiyse o ajan (Planning atlanır), susarsa <c>null</c> (Planning güvenlik ağı).
    /// </summary>
    private string? ResolveFirstAgent(ReasoningResult? reasoning, WorkflowTraceEventProcessor.TraceState st)
    {
        var mode = _routing?.CurrentValue.Mode ?? AgentRoutingMode.Planner;
        if (mode == AgentRoutingMode.Planner) return null;

        var decision = IntentRouter.Decide(reasoning);

        if (mode == AgentRoutingMode.Shadow)
        {
            // Alt görev koşusunda hedef bölme anında seçildi; Planning'le kıyaslamanın anlamı yok.
            if (decision.Reason != "subtask") st.ShadowRoute = decision;
            return null;
        }

        if (decision.Decided)
        {
            WorkflowMetrics.RecordRouting("deterministic", "routed", decision.Agent!, "-", decision.Reason);
            return decision.Agent;
        }

        WorkflowMetrics.RecordRouting("deterministic", "fallback_to_planner", "-", "-", decision.Reason);
        return null;
    }

    /// <summary>Gölge modun ölçümü: yönlendirici ↔ Planning karşılaştırması. Uyuşmazlık, düzeltilecek tablo satırının işaretidir.</summary>
    private void RecordShadowComparison(WorkflowTraceEventProcessor.TraceState st)
    {
        if (st.ShadowRoute is not { } route) return;

        var c = RoutingComparison.Compare(route, st.Trace.Planning);
        WorkflowMetrics.RecordRouting("shadow", c.Outcome, route.Agent ?? "-", c.PlannerAgent, route.Reason);
        if (c.Outcome == RoutingComparison.Mismatch)
            // Metin loglanmaz (kullanıcı verisi); yalnızca karar bileşenleri.
            _loggerFactory.CreateLogger<WorkflowRunner>().LogInformation(
                "[Routing] Gölge uyuşmazlığı | trace={TraceId} intent={Intent} router={Router}({Reason}) planner={Planner} requiredInfo={RequiredInfo}",
                st.Trace.TraceId, st.Trace.Reasoning?.Intent, route.Agent, route.Reason, c.PlannerAgent, st.Trace.Reasoning?.RequiredInfo.Count ?? 0);
    }

    private async Task<(string Result, string TerminationReason)> BuildFinalResultAsync(
        WorkflowTraceEventProcessor.TraceState st,
        AgentSession? session,
        string query,
        CancellationToken turnCt)
    {
        var terminationReason =
            WorkflowResponseExtractor.ParseTerminationReasonFromResult(
                st.Result, _loggerFactory.CreateLogger<WorkflowRunner>())
            ?? WellKnown.Termination.ReasonCompleted;

        if (st.IterationCount >= _guards.MaxIterations && TerminationProtocol.FindStart(st.Result) < 0)
        {
            terminationReason = WellKnown.Termination.ReasonMaxMessages;
            st.Result = "İşlemi bu turda güvenilir biçimde tamamlayamadım. Lütfen talebinizi netleştirin.";
        }

        RecordShadowComparison(st);

        // selfCritique HAM çıktıdan okunur — RemoveTechnicalJsonBlocks bloğu birazdan silecek.
        st.Trace.SelfCritique = SelfCritiqueParser.TryParse(st.Result);

        var result = WorkflowResponseExtractor.RemoveTerminationMarkers(st.Result);
        result = WorkflowResponseExtractor.RemoveTechnicalJsonBlocks(result);

        if (WorkflowResponseExtractor.ContainsAgentRoutingMessage(result))
        {
            try
            {
                result = await _messageBuilder.RewriteRoutingMessageAsync(result, query, turnCt);
            }
            catch (OperationCanceledException)
            {
                // Yeniden yazım KOZMETİK: elimizde zaten geçerli bir yanıt var, bu adım yalnızca
                // "X ajanına yönlendiriyorum" tarzı iç mesajı kullanıcı diline çeviriyor. Turun
                // bütçesi tam burada dolarsa yanıtın tamamını kaybetmek yerine ham metinle devam
                // ederiz. Bu try/catch olmadan, token'ları ortaklaştırmak streaming dalında
                // iterator'dan dışarı sızan bir iptal istisnası üretirdi.
                _loggerFactory.CreateLogger<WorkflowRunner>().LogWarning(
                    "Routing mesajı yeniden yazımı iptal edildi — ham metinle devam ediliyor.");
            }
        }

        // ConstrainedTargetAgent yalnızca decompose edilmiş ALT görev koşularında dolar
        // (SubTaskOrchestrator.CreateSubTaskReasoning) — yani "bu bir alt koşu" sinyali zaten
        // elimizde; ayrı bir bayrak taşımaya gerek yok. Alt koşularda tur bazlı yan etkiler
        // (episodic bellek, profil sayacı) atlanır; birleşik tur için bir kez yazılır.
        //
        // AgentWorkflowRegressionTests exercises this wiring with a real compound MAF run.
        var isSubTaskRun = st.Trace.Reasoning?.ConstrainedTargetAgent != null;

        await _finalizer.FinalizeAsync(
            st.Trace, session, query, result, terminationReason, turnCt, isSubTaskRun);

        return (result, terminationReason);
    }

    /// <summary>
    /// Bağlam parçalarını trace'e yazılabilir hâle çevirir — "model bu turda neyi biliyordu"
    /// sorusunun kaydı. Hata/timeout/bütçe yüzünden düşen parçalar da kaydedilir; asıl değeri
    /// orada, çünkü yanlış yanıtların sebebi çoğu zaman <b>eksik</b> bağlamdır.
    /// </summary>
    private static List<ContextPartUsage> ToContextUsage(ContextResult context) =>
        context.Parts.Select(p => new ContextPartUsage
        {
            ProviderName = p.ProviderName,
            Status = p.Status.ToString().ToLowerInvariant(),
            Length = p.Length
        }).ToList();

    private enum RunOutcomeKind { Completed, TimedOut, Cancelled, Error }

    private readonly record struct RunOutcome(RunOutcomeKind Kind, Exception? Error);

    /// <summary>
    /// Event döngüsü bittikten sonra anormal sonlanma durumlarını (timeout/iptal/hata) tek yerde
    /// ele alır — <see cref="RunAsync"/> ve <see cref="RunStreamingAsync"/> arasında bu blok
    /// birebir kopyaydı ve zamanla sessizce sapmıştı: non-streaming iptal dalı
    /// <c>ProcessPendingEscalationsAsync</c>'i hiç çağırmıyordu, streaming dalı çağırıyordu (timeout
    /// ve hata dalları ikisinde de çağırıyordu). Bu tutarsızlığın hangisinin "doğru" olduğuna
    /// karar vermek yerine — timeout/hata/streaming-iptal üçünün ortak davranışı (eskalasyonu
    /// işle) çoğunluk kuralıyla tek doğru davranış kabul edildi, non-streaming iptal buna
    /// hizalandı. Dönüş değeri her iki çağıranın da kendi tarzında (throw vs yield) tepki
    /// vermesini sağlar — bu metod kendi başına ne fırlatır ne yield eder.
    /// </summary>
    private async Task<RunOutcome> FinalizeAbnormalTerminationAsync(
        StreamingRun run,
        WorkflowTraceEventProcessor.TraceState st,
        string query,
        CancellationTokenSource timeoutCts,
        CancellationToken originalCt,
        Exception? workflowError)
    {
        st.Trace.IterationCount = st.IterationCount;

        if (timeoutCts.IsCancellationRequested && !originalCt.IsCancellationRequested)
        {
            await StopRunGracefullyAsync(run);
            await _approvalGate.ProcessPendingEscalationsAsync(st.Trace, query, "");
            await _traceStore.CompleteAsync(st.Trace.TraceId,
                terminationReason: WellKnown.Termination.ReasonTimeout,
                error: $"Workflow {_guards.TimeoutSeconds}s timeout'a takıldı");
            return new RunOutcome(RunOutcomeKind.TimedOut, null);
        }

        if (originalCt.IsCancellationRequested)
        {
            await StopRunGracefullyAsync(run);
            await _approvalGate.ProcessPendingEscalationsAsync(st.Trace, query, "");
            await _traceStore.CompleteAsync(st.Trace.TraceId,
                terminationReason: "cancelled",
                error: "İstek çağıran tarafından iptal edildi.");
            return new RunOutcome(RunOutcomeKind.Cancelled, null);
        }

        if (workflowError != null)
        {
            await _approvalGate.ProcessPendingEscalationsAsync(st.Trace, query, "");
            // Trace'e (admin/debug amaçlı) HAM hata metni yazılır — istemciye giden metin
            // ayrı, sanitize edilmiş bir yoldan gelir (bkz. RunAsync/RunStreamingAsync'te
            // ExceptionTranslator.Translate çağrıları).
            await _traceStore.CompleteAsync(st.Trace.TraceId, terminationReason: "error", error: workflowError.Message);
            return new RunOutcome(RunOutcomeKind.Error, workflowError);
        }

        return new RunOutcome(RunOutcomeKind.Completed, null);
    }

    /// <summary>
    /// Timeout/iptal anında koşuyu durdurur: sonraki ajan turunun başlamasını engeller VE
    /// hâlihazırda devam eden ajan/LLM çağrısına iptali yayar.
    ///
    /// <para>
    /// Bu ikinci kısım MAF 1.17.0'da ölçüldü: LLM çağrısının içindeyken <c>CancelRunAsync()</c>
    /// çağrıldığında çağrıya verilen <c>CancellationToken</c> milisaniyeler içinde iptal
    /// ediliyor; çağrılmadığında iptal edilmiyor (kontrol deneyiyle nedensellik doğrulandı).
    /// Soketin gerçekten kapanıp kapanmadığı alttaki HTTP istemcisine bağlıdır — ölçülen şey
    /// framework'ün iptali <b>yaydığı</b>dır.
    /// </para>
    ///
    /// <para>
    /// ⚠️ Bu yorum eskiden "MAF (1.15.0) devam eden LLM çağrısını kesmez (framework
    /// sınırlaması)" diyordu; 1.17.0'da bu <b>artık doğru değil</b>. Yanlış bilgiye dayanıp
    /// olmayan bir kısıt için çözüm yazılmasın diye kayda geçiriliyor. Sürüm yükseltmelerinde
    /// yeniden ölçün.
    /// </para>
    ///
    /// Hata olursa yutulur; bu zaten en iyi çaba (best-effort) bir temizliktir.
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

    /// <summary>
    /// HITL onay köprüsü — <b>bugün ULAŞILAMAZ kod</b>, bilerek korunuyor.
    ///
    /// <para>
    /// <b>Neden ulaşılamaz:</b> bu metot yalnızca bir tool <c>ApprovalRequiredAIFunction</c>
    /// ile sarılırsa tetiklenir. Repo genelinde production kodunda böyle bir sarma YOK —
    /// tüm tool'lar düz <c>AIFunctionFactory.Create</c> ile kuruluyor
    /// (<c>ApprovalGateService.Build*Tool</c>), çünkü onay bloklamayan modele geçti.
    /// Tek gerçek örnekleme bir testte: <c>HitlRejectionFormatTests</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Neden silinmiyor:</b> (1) canlı tutmanın topolojik maliyeti sıfır — ölçüldü
    /// (MAF 1.17.0): bir ajana <c>ApprovalRequiredAIFunction</c> eklemek workflow graf'ına
    /// düğüm veya port EKLEMİYOR, id listesi değişmiyor. (2) Silinirse ve ileride biri bir
    /// tool'u o modelde sararsa, üretilen <c>RequestInfoEvent</c> işlenmeden kalır; superstep
    /// yanıt bekler ve o tur timeout'a kadar <b>asılı kalır</b>. Yani köprü, silinmesi
    /// gereken ölü kod değil, açık bırakılmış bir emniyet valfidir.
    /// </para>
    ///
    /// <para>
    /// <b>Yeniden devreye girmesi için</b> tek gereken, ilgili tool'u
    /// <c>ApprovalGateService</c>'te <c>ApprovalRequiredAIFunction</c> ile sarmak; buradaki
    /// kod değişmeden çalışır. Ama önce bunun neden terk edildiğine bakın: admin kararını
    /// turun içinde beklemek, onaylar birikince <c>TimeoutSeconds</c> içinde yetişilememesine
    /// ve isteklerin sessizce otomatik red'e düşmesine yol açıyordu.
    /// </para>
    ///
    /// Bir tool ApprovalRequiredAIFunction ile sarılırsa, FunctionInvokingChatClient onu
    /// GERÇEKTEN ÇALIŞTIRMADAN önce bir ToolApprovalRequestContent üretir; bu da
    /// AIAgentHostExecutor tarafından workflow superstep'ini duraklatan gerçek bir
    /// RequestInfoEvent'e dönüşür (GroupChatWorkflowBuilder ile kurulan her ajan bunu
    /// otomatik destekler — ek graph kablolaması gerekmez). Burada event yakalanıp
    /// ApprovalGateService.RequestApprovalAsync ile aynı IApprovalQueue/SSE/SLA altyapısı
    /// tetiklenir.
    ///
    /// Bilinmeyen/parse edilemeyen bir RequestInfoEvent gelirse (ör. framework ileride başka
    /// tür request'ler eklerse) sessizce atlanır — hiçbir yanıt gönderilmez, o superstep askıda
    /// kalır; bu, bugünkü sürümde sadece approval-request türü beklendiği için kabul edilen bir
    /// sınır durumdur.
    ///
    /// NOT: Bu köprü artık yalnızca (varsa) hâlâ ApprovalRequiredAIFunction ile sarılı tool'lar
    /// için tetiklenir — sipariş/iade/iptal/şikayet tool'ları (bkz. ApprovalGateService) artık
    /// bloklamayan modele geçti ve bu event'i hiç üretmiyor.
    /// </summary>
    private async Task HandleRequestInfoEventAsync(
        StreamingRun run, RequestInfoEvent requestInfo, WorkflowTraceEventProcessor.TraceState st, CancellationToken ct)
    {
        if (!requestInfo.Request.TryGetDataAs<ToolApprovalRequestContent>(out var approvalRequest))
            return;

        if (approvalRequest.ToolCall is not FunctionCallContent functionCall)
            return;

        var decision = await _approvalGate.RequestApprovalAsync(
            toolName: functionCall.Name,
            agentName: ApprovalGateService.ResolveAgentName(functionCall.Name),
            parameters: functionCall.Arguments,
            justification: WorkflowTraceEventProcessor.ResolveApprovalJustification(st),
            ct);

        var responseContent = approvalRequest.CreateResponse(decision.Approved, decision.Reason);
        var externalResponse = requestInfo.Request.CreateResponse(responseContent);
        await run.SendResponseAsync(externalResponse);
    }

    /// <summary>
    /// MAF workflow olaylarını güvenle numaralandırır — <c>MoveNextAsync</c>'ten sızabilecek
    /// bir exception akışı kesip çağırana anlamlı bir sonuç olarak taşır.
    ///
    /// <para>
    /// 🐞 <b>Eskiden yalnızca <c>ex.Message</c> taşınıyordu</b>, gerçek exception nesnesi değil.
    /// Bu iki soruna yol açıyordu: (1) çağıran hiçbir zaman <c>ExceptionTranslator</c>'ın tür
    /// bazlı ayrımını (bugün kaldırıldı, bkz. <see cref="ExceptionTranslator"/>) kullanamıyordu,
    /// (2) daha önemlisi, streaming yolu bu ham mesajı hiçbir sanitizasyondan geçirmeden
    /// doğrudan istemciye <c>StreamEvent(Error, ...)</c> olarak gönderiyordu — framework/HTTP
    /// iç detayları (ör. bağlantı adresleri, stack içerikleri message'a sızmışsa) kullanıcıya
    /// görünür oluyordu. Artık gerçek <see cref="Exception"/> taşınır; çağıranlar onu
    /// <see cref="ExceptionTranslator.Translate"/> ile sanitize edip kullanıcıya güvenli bir
    /// metin, trace'e ise (admin/debug amaçlı) ham detayı yazar.
    /// </para>
    /// </summary>
    private static async IAsyncEnumerable<(WorkflowEvent? evt, Exception? error)>
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
                Exception? error = null;
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
                    error = ex;
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

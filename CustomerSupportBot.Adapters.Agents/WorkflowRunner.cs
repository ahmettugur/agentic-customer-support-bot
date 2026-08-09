// Adapters.Agents/WorkflowRunner.cs
// Tek bir (decompose edilmemiş) kullanıcı sorgusu için GroupChat workflow koşusu:
// workflow execution + event loop orkestrasyonu. Mesaj hazırlığı WorkflowMessageBuilder'a,
// trace/event işleme WorkflowTraceEventProcessor'a taşındı (#47 — bölme) — bu sınıf artık
// yalnızca "koşuyu başlat, event'leri dinle, anormal sonlanmayı yönet" sorumluluğunu taşır.
// Compound query'lerin alt görevlere bölünmesi DecomposedRunner'ın sorumluluğundadır —
// o da her alt görev için bu sınıfın RunAsync/RunStreamingAsync'ini çağırır.

using System.Runtime.CompilerServices;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Domain.Model;
using AgentSession = CustomerSupportBot.Domain.Model.AgentSession;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Agents;

internal sealed class WorkflowRunner
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

    public WorkflowRunner(
        AgentTeamFactory factory,
        TurnFinalizer finalizer,
        WorkflowGuardOptions guards,
        IReasoningTraceStore traceStore,
        ApprovalGateService approvalGate,
        IUiHintEmitter uiHint,
        ILoggerFactory loggerFactory,
        WorkflowTraceEventProcessor traceProcessor,
        WorkflowMessageBuilder messageBuilder)
    {
        _factory = factory;
        _finalizer = finalizer;
        _guards = guards;
        _traceStore = traceStore;
        _approvalGate = approvalGate;
        _uiHint = uiHint;
        _loggerFactory = loggerFactory;
        _traceProcessor = traceProcessor;
        _messageBuilder = messageBuilder;
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
        var messages = await _messageBuilder.BuildWorkflowMessagesAsync(query, conversationHistory, session, reasoning);

        using var timeoutCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(_guards.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var effectiveCt = linkedCts.Token;

        var st = _traceProcessor.StartTraceState(session, query, reasoning);

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

            if (evt is RequestInfoEvent requestInfo)
            {
                await HandleRequestInfoEventAsync(run, requestInfo, st, effectiveCt);
                continue;
            }

            if (evt is WorkflowErrorEvent errorEvt)
            {
                workflowError = errorEvt.Exception?.Message ?? "workflow error";
                break;
            }

            _traceProcessor.ApplyTraceEvent(st, evt);
        }

        var outcome = await FinalizeAbnormalTerminationAsync(run, st, query, timeoutCts, ct, workflowError);
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
                    new InvalidOperationException(outcome.ErrorMessage),
                    "RunAsync workflow hatası.");
        }

        var terminationReason =
            WorkflowResponseExtractor.ParseTerminationReasonFromResult(
                st.Result, _loggerFactory.CreateLogger<WorkflowRunner>())
            ?? WellKnown.Termination.ReasonCompleted;

        var result = WorkflowResponseExtractor.RemoveTerminationMarkers(st.Result);
        result = WorkflowResponseExtractor.RemoveTechnicalJsonBlocks(result);

        if (WorkflowResponseExtractor.ContainsAgentRoutingMessage(result))
        {
            result = await _messageBuilder.RewriteRoutingMessageAsync(result, query, ct);
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

        var messages = await _messageBuilder.BuildWorkflowMessagesAsync(query, conversationHistory, session, reasoning);

        using var timeoutCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(_guards.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var effectiveCt = linkedCts.Token;

        var st = _traceProcessor.StartTraceState(session, query, reasoning);

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

            if (evt is RequestInfoEvent requestInfo)
            {
                await HandleRequestInfoEventAsync(run, requestInfo, st, effectiveCt);
                continue;
            }

            if (evt is WorkflowErrorEvent errorEvt)
            {
                workflowError = errorEvt.Exception?.Message ?? "workflow error";
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

        var outcome = await FinalizeAbnormalTerminationAsync(run, st, query, timeoutCts, ct, workflowError);
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
                yield return new StreamEvent(StreamEventTypes.Error,
                    new { message = outcome.ErrorMessage });
                yield break;
        }

        var terminationReason =
            WorkflowResponseExtractor.ParseTerminationReasonFromResult(
                st.Result, _loggerFactory.CreateLogger<WorkflowRunner>())
            ?? WellKnown.Termination.ReasonCompleted;

        var result = WorkflowResponseExtractor.RemoveTerminationMarkers(st.Result);
        result = WorkflowResponseExtractor.RemoveTechnicalJsonBlocks(result);

        if (WorkflowResponseExtractor.ContainsAgentRoutingMessage(result))
            result = await _messageBuilder.RewriteRoutingMessageAsync(result, query, effectiveCt);

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

    private enum RunOutcomeKind { Completed, TimedOut, Cancelled, Error }

    private readonly record struct RunOutcome(RunOutcomeKind Kind, string? ErrorMessage);

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
        string? workflowError)
    {
        st.Trace.IterationCount = st.IterationCount;

        if (timeoutCts.IsCancellationRequested && !originalCt.IsCancellationRequested)
        {
            await StopRunGracefullyAsync(run);
            await _approvalGate.ProcessPendingEscalationsAsync(st.Trace, query, "");
            _traceStore.Complete(st.Trace.TraceId,
                terminationReason: WellKnown.Termination.ReasonTimeout,
                error: $"Workflow {_guards.TimeoutSeconds}s timeout'a takıldı");
            return new RunOutcome(RunOutcomeKind.TimedOut, null);
        }

        if (originalCt.IsCancellationRequested)
        {
            await StopRunGracefullyAsync(run);
            await _approvalGate.ProcessPendingEscalationsAsync(st.Trace, query, "");
            _traceStore.Complete(st.Trace.TraceId,
                terminationReason: "cancelled",
                error: "İstek çağıran tarafından iptal edildi.");
            return new RunOutcome(RunOutcomeKind.Cancelled, null);
        }

        if (workflowError != null)
        {
            await _approvalGate.ProcessPendingEscalationsAsync(st.Trace, query, "");
            _traceStore.Complete(st.Trace.TraceId, terminationReason: "error", error: workflowError);
            return new RunOutcome(RunOutcomeKind.Error, workflowError);
        }

        return new RunOutcome(RunOutcomeKind.Completed, null);
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

    /// <summary>
    /// HITL onay köprüsü. ApprovalGateService, yan etkili tool'ları ApprovalRequiredAIFunction
    /// ile sarmalıyor — FunctionInvokingChatClient bu tool'ları GERÇEKTEN ÇALIŞTIRMADAN önce
    /// bir ToolApprovalRequestContent üretiyor, bu da AIAgentHostExecutor tarafından workflow
    /// superstep'ini duraklatan gerçek bir RequestInfoEvent'e dönüşüyor (GroupChatWorkflowBuilder
    /// ile kurulan her ajan bunu otomatik destekliyor — ek graph kablolaması gerekmez).
    ///
    /// Burada event'i yakalayıp ApprovalGateService.RequestApprovalAsync ile AYNI
    /// IApprovalQueue/SSE/SLA altyapısını tetikliyoruz (admin paneli, eskalasyon, SLA guardian
    /// hiç değişmedi — sadece "kim bekliyor" değişti: eskiden tool lambda'sının içindeki bir
    /// Task, şimdi framework'ün kendi checkpoint'lenebilir superstep duraklaması).
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

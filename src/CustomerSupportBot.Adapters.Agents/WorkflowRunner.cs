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
        var messages = prompt.Messages;

        var st = _traceProcessor.StartTraceState(session, query, reasoning);
        st.Trace.EstimatedTokens = TokenEstimator.Estimate(messages.Select(m => m.Text));
        st.Trace.ContextParts = ToContextUsage(prompt.Context);

        var workflow = _factory.CreateWorkflow(reasoning?.ConstrainedTargetAgent);
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

        try
        {
            var (result, _) = await BuildFinalResultAsync(st, session, query, effectiveCt);
            return result;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _traceStore.Complete(st.Trace.TraceId,
                terminationReason: WellKnown.Termination.ReasonTimeout,
                error: $"Workflow {_guards.TimeoutSeconds}s timeout'a takıldı during finalization");
            throw ExceptionTranslator.Translate(
                new TimeoutException($"Workflow {_guards.TimeoutSeconds}s timeout'a takıldı."),
                "RunAsync workflow finalization timeout.");
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
            var message = timeoutCts.IsCancellationRequested
                ? $"İşlem {_guards.TimeoutSeconds} saniyede tamamlanamadı."
                : promptError.Message;
            yield return new StreamEvent(StreamEventTypes.Error, new { message });
            yield break;
        }

        var messages = prompt!.Messages;

        var st = _traceProcessor.StartTraceState(session, query, reasoning);
        st.Trace.EstimatedTokens = TokenEstimator.Estimate(messages.Select(m => m.Text));
        st.Trace.ContextParts = ToContextUsage(prompt.Context);

        var workflow = _factory.CreateWorkflow(reasoning?.ConstrainedTargetAgent);
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
            var message = timedOut
                ? $"İşlem {_guards.TimeoutSeconds} saniyede tamamlanamadı."
                : finalizationError.Message;
            _traceStore.Complete(st.Trace.TraceId,
                terminationReason: timedOut
                    ? WellKnown.Termination.ReasonTimeout
                    : WellKnown.Termination.ReasonError,
                error: message);
            yield return new StreamEvent(StreamEventTypes.Error, new { message });
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

        // Tipli payload: bu metin turun KANONİK yanıtıdır ve delta akışından farklı olabilir
        // (delta'lar ham, bu metin temizlenmiş/yeniden yazılmış). ChatPortService ve
        // RealtimeBridgeService kalıcılaştırma ve TTS için bunu okur — bkz. ResponseCompletePayload.
        yield return new StreamEvent(StreamEventTypes.ResponseComplete,
            new ResponseCompletePayload(result, terminationReason));
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
        // ⚠️ TEST BOŞLUĞU: bu SATIRIN kendisi test altında değil. Zincirin iki ucu test
        // ediliyor — CreateSubTaskReasoning alanı dolduruyor (SubTaskOrchestratorTests) ve
        // TurnFinalizer bayrağa uyuyor (CompoundTurnFinalizationTests) — ama ikisini bağlayan
        // burası açıkta. Ölçüldü: bu satır `false` yapıldığında hiçbir test düşmüyor.
        //
        // Kapatma DENENDİ ve maliyeti ölçüldü: gerçek MAF workflow'unu sahte bir IChatClient
        // ile koşturmak, sahte modelin TÜM ajan sözleşmelerini birden taklit etmesini
        // gerektiriyor (planning JSON, specialist JSON, response metni + TERMINATE, ve doğru
        // routing kararları). Şema uyumlu JSON'larla bile grup sohbeti sonlanmadı: tek turda
        // ~13.900 model çağrısı yapılıp guard timeout'una düşüldü. Böyle bir test, koruduğu tek
        // satırdan çok daha kırılgan olurdu — prompt'lardaki her değişiklik onu bozardı.
        //
        // Gerçek kapatma yolu: workflow'a enjekte edilebilir bir "senaryo ajanı" (ChatClientAgent
        // yerine deterministik AIAgent) desteği. Bu, üretim kodunda test için bir kanca demek
        // ve ayrı bir tasarım kararı.
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

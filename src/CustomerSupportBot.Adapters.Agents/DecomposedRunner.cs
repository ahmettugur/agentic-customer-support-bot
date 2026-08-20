// Adapters.Agents/DecomposedRunner.cs
// Compound query (bileşik sorgu) orkestrasyonu: reasoning'in ürettiği SubTasks listesini
// yan-etkisiz/yan-etkili gruplara ayırır (bkz. ParallelExecutionOptions), her grubu
// WorkflowRunner üzerinden (paralel veya sıralı) çalıştırır ve sonuçları birleştirir.

using System.Runtime.CompilerServices;
using System.Text;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Services.Reasoning;
using CustomerSupportBot.Domain.Model;
using AgentSession = CustomerSupportBot.Domain.Model.AgentSession;

namespace CustomerSupportBot.Adapters.Agents;

internal sealed class DecomposedRunner
{
    private readonly IWorkflowRunner _runner;
    private readonly ParallelExecutionOptions _parallelOptions;
    private readonly IUiHintEmitter _uiHint;
    private readonly IApprovalContextAccessor _approvalContext;
    private readonly TurnFinalizer _finalizer;

    public DecomposedRunner(
        IWorkflowRunner runner,
        ParallelExecutionOptions parallelOptions,
        IUiHintEmitter uiHint,
        IApprovalContextAccessor approvalContext,
        TurnFinalizer finalizer)
    {
        _runner = runner;
        _parallelOptions = parallelOptions;
        _uiHint = uiHint;
        _approvalContext = approvalContext;
        _finalizer = finalizer;
    }

    public async Task<string> RunDecomposedAsync(
        string query,
        List<ConversationMessage>? conversationHistory,
        AgentSession? session,
        ReasoningResult reasoning,
        CancellationToken ct)
    {
        var ordered = SubTaskOrchestrator.ValidateExecutionPlan(reasoning, query, _parallelOptions);
        var baseHistory = conversationHistory != null
            ? new List<ConversationMessage>(conversationHistory)
            : new List<ConversationMessage>();
        var completed = new Dictionary<int, CompletedSubTask>();
        var groups = SubTaskOrchestrator.Partition(ordered, _parallelOptions);
        var collected = new SortedDictionary<int, string>();

        using var timeoutCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(_parallelOptions.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var effectiveCt = linkedCts.Token;

        try
        {
            foreach (var group in groups)
            {
                if (group.Parallel && group.Items.Count > 1)
                {
                    using var sem = new SemaphoreSlim(
                        Math.Max(1, _parallelOptions.MaxDegreeOfParallelism));

                    var tasks = group.Items.Select(async sub =>
                    {
                        await sem.WaitAsync(effectiveCt).ConfigureAwait(false);
                        try
                        {
                            var subQuery = SubTaskOrchestrator.FormatSubTaskQuery(sub);
                            var subReasoning = SubTaskOrchestrator.CreateSubTaskReasoning(reasoning, sub);
                            var history = BuildSubTaskHistory(baseHistory, sub, completed);
                            var subResp = await _runner.RunAsync(
                                    subQuery, history, session, subReasoning, effectiveCt)
                                .ConfigureAwait(false);
                            return (sub, subQuery, subResp);
                        }
                        finally { sem.Release(); }
                    });

                    var results = await Task.WhenAll(tasks).ConfigureAwait(false);
                    foreach (var (sub, subQuery, resp) in results.OrderBy(t => t.sub.Order))
                    {
                        collected[sub.Order] = SubTaskOrchestrator.FormatSubTaskResult(sub, resp);
                        completed[sub.Order] = new CompletedSubTask(subQuery, resp);
                    }
                }
                else
                {
                    foreach (var sub in group.Items)
                    {
                        var subQuery = SubTaskOrchestrator.FormatSubTaskQuery(sub);
                        var subReasoning = SubTaskOrchestrator.CreateSubTaskReasoning(reasoning, sub);
                        var history = BuildSubTaskHistory(baseHistory, sub, completed);
                        var subResp = await _runner.RunAsync(
                                subQuery, history, session, subReasoning, effectiveCt)
                            .ConfigureAwait(false);
                        collected[sub.Order] = SubTaskOrchestrator.FormatSubTaskResult(sub, subResp);
                        completed[sub.Order] = new CompletedSubTask(subQuery, subResp);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Compound workflow {_parallelOptions.TimeoutSeconds}s timeout'a takıldı.");
        }

        var aggregate = SubTaskOrchestrator.AggregateSubTaskResults(collected.Values.ToList());

        // Tur bazlı yan etkiler BURADA, bir kez. Alt koşular bunları atlar
        // (bkz. TurnFinalizer.FinalizeAsync → isSubTaskRun): aksi hâlde tek bir kullanıcı
        // mesajı profil sayacını N tur ilerletir ve N kopuk episode yazardı.
        await _finalizer.FinalizeAggregateTurnAsync(session, query, aggregate, reasoning.Intent, ct);

        return aggregate;
    }

    public async IAsyncEnumerable<StreamEvent> RunDecomposedStreamingAsync(
        string query,
        List<ConversationMessage>? conversationHistory,
        AgentSession? session,
        ReasoningResult reasoning,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var sessionId = session?.SessionId ?? string.Empty;
        List<SubTask>? ordered = null;
        string? validationError = null;
        try
        {
            ordered = SubTaskOrchestrator.ValidateExecutionPlan(reasoning, query, _parallelOptions);
        }
        catch (Exception ex)
        {
            validationError = ex.Message;
        }

        if (validationError != null)
        {
            yield return new StreamEvent(StreamEventTypes.Error, new { message = validationError });
            yield break;
        }

        var baseHistory = conversationHistory != null
            ? new List<ConversationMessage>(conversationHistory)
            : new List<ConversationMessage>();
        var completed = new Dictionary<int, CompletedSubTask>();

        var collected = new SortedDictionary<int, string>();
        var total = ordered!.Count;
        var groups = SubTaskOrchestrator.Partition(ordered, _parallelOptions);
        var parallelGroupCount = groups.Count(g => g.Parallel && g.Items.Count > 1);

        using var timeoutCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(_parallelOptions.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var effectiveCt = linkedCts.Token;

        // Sıralı gruplar gerçek token akışıyla, paralel gruplar ise batch tamamlandıktan sonra
        // deterministik sırayla yayınlanır. Bu bayrak yalnızca parçalar arasındaki ayırıcı içindir.
        var anyPartEmitted = false;

        // response_start, ilk delta'dan ÖNCE gönderilir — "yanıt metni akmaya başlıyor"
        // anlamı her iki yolda (tek sorgu / compound) aynı kalsın diye. Bu olayın sırasına
        // güvenen bir tüketici compound'da sessizce yanılmamalı.
        //
        // decomposed=true bayrağı burada işlevsel: Blazor bu olayda normalde ajan çiplerini
        // MÜHÜRLÜYOR (Chat.razor → SealAgentChips), ama compound'da alt görevler metin akarken
        // de çalışmaya devam eder — erken mühür "SubTask#N" ilerleme çiplerini yok ederdi.
        // Bayrağı gören Blazor mühürlemeyi response_complete'e erteler.
        yield return new StreamEvent(StreamEventTypes.ResponseStart,
            new
            {
                terminationReason = WellKnown.Termination.ReasonCompleted,
                revised = false,
                decomposed = true,
                subTaskCount = total
            });

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

                using var sem = new SemaphoreSlim(
                    Math.Max(1, _parallelOptions.MaxDegreeOfParallelism));

                var tasks = group.Items.Select(async sub =>
                {
                    await sem.WaitAsync(effectiveCt).ConfigureAwait(false);
                    try
                    {
                        var subQuery = SubTaskOrchestrator.FormatSubTaskQuery(sub);
                        var subReasoning = SubTaskOrchestrator.CreateSubTaskReasoning(reasoning, sub);
                        var history = BuildSubTaskHistory(baseHistory, sub, completed);
                        // Bu paralel dalın kendi (izole) async akışında ambient ajan adını
                        // sub.TargetAgent'a sabitle — RunAsync hint drain etmediği için
                        // (streaming değil), ürettiği ipuçları burada, kendi TargetAgent'ıyla
                        // etiketlenmiş biçimde kuyrukta bekler.
                        _approvalContext.SetCurrentAgent(sub.TargetAgent);
                        var resp = await _runner.RunAsync(subQuery, history, session, subReasoning, effectiveCt)
                            .ConfigureAwait(false);
                        var hints = _uiHint.DrainPending(sessionId);
                        return (sub, subQuery, resp, hints);
                    }
                    finally { sem.Release(); }
                }).ToList();

                (SubTask sub, string subQuery, string resp, IReadOnlyList<StreamEvent> hints)[]? results = null;
                Exception? batchError = null;
                try
                {
                    results = await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    batchError = ex;
                }

                if (batchError != null)
                {
                    if (ct.IsCancellationRequested) yield break;
                    var message = timeoutCts.IsCancellationRequested
                        ? $"Compound işlem {_parallelOptions.TimeoutSeconds} saniyede tamamlanamadı."
                        : batchError.Message;
                    yield return new StreamEvent(StreamEventTypes.Error, new { message });
                    yield break;
                }

                foreach (var (sub, subQuery, resp, hints) in results!.OrderBy(t => t.sub.Order))
                {
                    var part = SubTaskOrchestrator.FormatSubTaskResult(sub, resp);
                    collected[sub.Order] = part;
                    completed[sub.Order] = new CompletedSubTask(subQuery, resp);

                    foreach (var hint in hints)
                        yield return hint;

                    yield return new StreamEvent(StreamEventTypes.Agent,
                        new { name = $"SubTask#{sub.Order}", status = "done", order = sub.Order });

                    // Task.WhenAll tamamlandı; sonuçları kanonik sub.Order sırasında yayınla.
                    if (anyPartEmitted)
                        yield return new StreamEvent(StreamEventTypes.ResponseDelta,
                            new TextDeltaPayload(SubTaskOrchestrator.ResultSeparator));
                    yield return new StreamEvent(StreamEventTypes.ResponseDelta, new TextDeltaPayload(part));
                    anyPartEmitted = true;
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
                    var history = BuildSubTaskHistory(baseHistory, sub, completed);
                    var subResponseBuilder = new StringBuilder();

                    // Sıralı dalda alt görevin GERÇEK token akışı kullanıcıya canlı iletilir —
                    // paralel daldan farkı, burada sonuçların sırayla üretilmesi, yani araya
                    // girmeden akıtılabilmesi. Başlık gövdeden önce yayınlanır çünkü gövde
                    // henüz üretilmedi; TrimmingDeltaStreamer ise ham token'ları
                    // FormatSubTaskResult'ın uyguladığı Trim() ile BİREBİR aynı hâle getirir,
                    // böylece akan metnin birleşimi nihai metne eşit kalır.
                    if (anyPartEmitted)
                        yield return new StreamEvent(StreamEventTypes.ResponseDelta,
                            new TextDeltaPayload(SubTaskOrchestrator.ResultSeparator));
                    yield return new StreamEvent(StreamEventTypes.ResponseDelta,
                        new TextDeltaPayload(SubTaskOrchestrator.FormatSubTaskHeader(sub)));
                    anyPartEmitted = true;

                    var bodyStreamer = new TrimmingDeltaStreamer();
                    var subTaskFailed = false;

                    // Alt görevin KANONİK sonucu. Ham delta birleşimi bunun yerine
                    // kullanılamaz: terminal JSON temizliği ve routing canonicalization
                    // response_complete'te uygulanır, delta'larda değil. Sonuç hem geçmişe
                    // hem de BAĞIMLI alt görevlerin girdisine gittiği için, ham metin
                    // kullanmak temizlenmemiş JSON'u ve ajan adı sızıntısını bir sonraki
                    // alt görevin bağlamına taşıyordu.
                    string? subCanonical = null;

                    _approvalContext.SetCurrentAgent(sub.TargetAgent);
                    string? subTaskException = null;
                    await foreach (var (evt, error) in EnumerateSubTaskEventsSafely(
                        _runner.RunStreamingAsync(
                            subQuery, history, session, subReasoning, effectiveCt),
                        effectiveCt))
                    {
                        if (error != null)
                        {
                            subTaskException = error;
                            break;
                        }
                        if (evt == null) continue;

                        switch (evt.Type)
                        {
                            case var t when t == StreamEventTypes.ResponseDelta:
                            {
                                var raw = WorkflowResponseExtractor.ExtractDeltaText(evt.Data);
                                subResponseBuilder.Append(raw);
                                var safe = bodyStreamer.Feed(raw);
                                if (safe.Length > 0)
                                    yield return new StreamEvent(StreamEventTypes.ResponseDelta,
                                        new TextDeltaPayload(safe));
                                break;
                            }
                            case var t when t == StreamEventTypes.ResponseComplete:
                                if (evt.Data is ResponseCompletePayload complete
                                    && !string.IsNullOrWhiteSpace(complete.Text))
                                {
                                    subCanonical = complete.Text;
                                }
                                break;
                            case var t when t == StreamEventTypes.ResponseStart:
                                break;
                            case var t when t == StreamEventTypes.ReasoningStart
                                         || t == StreamEventTypes.ReasoningDelta
                                         || t == StreamEventTypes.ReasoningComplete:
                                break;
                            case var t when t == StreamEventTypes.Error:
                                subTaskFailed = true;
                                yield return evt;
                                break;
                            default:
                                yield return evt;
                                break;
                        }
                        if (subTaskFailed) break;
                    }

                    if (subTaskException != null)
                    {
                        if (ct.IsCancellationRequested) yield break;
                        var message = timeoutCts.IsCancellationRequested
                            ? $"Compound işlem {_parallelOptions.TimeoutSeconds} saniyede tamamlanamadı."
                            : subTaskException;
                        yield return new StreamEvent(StreamEventTypes.Error, new { message });
                        yield return new StreamEvent(StreamEventTypes.Agent,
                            new { name = $"SubTask#{sub.Order}", status = "failed", order = sub.Order });
                        yield break;
                    }

                    if (subTaskFailed)
                    {
                        yield return new StreamEvent(StreamEventTypes.Agent,
                            new { name = $"SubTask#{sub.Order}", status = "failed", order = sub.Order });
                        yield break;
                    }

                    if (ct.IsCancellationRequested) yield break;
                    if (timeoutCts.IsCancellationRequested)
                    {
                        yield return new StreamEvent(StreamEventTypes.Error,
                            new { message = $"Compound işlem {_parallelOptions.TimeoutSeconds} saniyede tamamlanamadı." });
                        yield break;
                    }

                    // Kanonik metin varsa o; yoksa güvenlik ağı olarak delta birleşimi —
                    // response_complete hiç gelmediğinde sonuç tamamen kaybolmamalı.
                    var subResponse = (subCanonical ?? subResponseBuilder.ToString()).Trim();
                    var part = SubTaskOrchestrator.FormatSubTaskResult(sub, subResponse);
                    collected[sub.Order] = part;
                    completed[sub.Order] = new CompletedSubTask(subQuery, subResponse);

                    yield return new StreamEvent(StreamEventTypes.Agent,
                        new { name = $"SubTask#{sub.Order}", status = "done", order = sub.Order });
                    // Metin burada yayınlanmaz — başlık + gövde zaten canlı akıtıldı.
                }
            }
        }

        yield return new StreamEvent(StreamEventTypes.Agent,
            new { name = "Orchestrator", status = "aggregating" });

        var aggregated = SubTaskOrchestrator.AggregateSubTaskResults(collected.Values.ToList());

        // Metin burada TEKRAR yayınlanmaz — parçalar tamamlandıkça zaten gönderildi ve
        // birleşimleri tam olarak `aggregated`a eşit (bkz. SubTaskOrchestrator.ResultSeparator).
        // Tur bazlı yan etkiler streaming yolda da BİR KEZ yazılır.
        //
        // Alt koşular bunları atlar (TurnFinalizer.FinalizeAsync → isSubTaskRun). Bu çağrı
        // eklenmeden önce streaming compound turda profil/episodic kaydı N değil SIFIR
        // oluyordu — yani düzeltme bir sorunu (N kayıt) başkasıyla (hiç kayıt) değiştirmişti.
        // Asıl arayüz /chat/stream kullandığı için etkilenen yol da buydu.
        await _finalizer.FinalizeAggregateTurnAsync(session, query, aggregated, reasoning.Intent, ct);

        yield return new StreamEvent(StreamEventTypes.ResponseComplete,
            new ResponseCompletePayload(
                aggregated,
                TerminationReason: WellKnown.Termination.ReasonCompleted,
                Revised: false,
                Decomposed: true,
                SubTaskCount: total));
    }

    private static List<ConversationMessage> BuildSubTaskHistory(
        IReadOnlyCollection<ConversationMessage> baseHistory,
        SubTask sub,
        IReadOnlyDictionary<int, CompletedSubTask> completed)
    {
        var history = new List<ConversationMessage>(baseHistory);
        foreach (var dependency in sub.Dependencies.Order())
        {
            if (!completed.TryGetValue(dependency, out var result))
                throw new InvalidOperationException(
                    $"Alt görev {sub.Order} bağımlılığı tamamlanmadan başlatıldı: {dependency}.");

            history.Add(new ConversationMessage(ConversationRoles.User, result.Query));
            history.Add(new ConversationMessage(ConversationRoles.Assistant, result.Response));
        }
        return history;
    }

    private sealed record CompletedSubTask(string Query, string Response);

    private static async IAsyncEnumerable<(StreamEvent? Event, string? Error)>
        EnumerateSubTaskEventsSafely(
            IAsyncEnumerable<StreamEvent> stream,
            [EnumeratorCancellation] CancellationToken ct)
    {
        var enumerator = stream.GetAsyncEnumerator(ct);
        try
        {
            while (true)
            {
                StreamEvent? current = null;
                string? error = null;
                try
                {
                    if (!await enumerator.MoveNextAsync()) yield break;
                    current = enumerator.Current;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    error = "Alt görev iptal edildi.";
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
            try
            {
                await enumerator.DisposeAsync();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Üst akış iptali zaten error/cancel sonucu olarak normalize edildi.
            }
        }
    }
}

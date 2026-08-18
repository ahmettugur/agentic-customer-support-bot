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

    public DecomposedRunner(
        IWorkflowRunner runner,
        ParallelExecutionOptions parallelOptions,
        IUiHintEmitter uiHint,
        IApprovalContextAccessor approvalContext)
    {
        _runner = runner;
        _parallelOptions = parallelOptions;
        _uiHint = uiHint;
        _approvalContext = approvalContext;
    }

    public async Task<string> RunDecomposedAsync(
        string query,
        List<ConversationMessage>? conversationHistory,
        AgentSession? session,
        ReasoningResult reasoning,
        CancellationToken ct)
    {
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
                        var subResp = await _runner.RunAsync(subQuery, historySnapshot, session, subReasoning, ct)
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
                    var subResp = await _runner.RunAsync(subQuery, runningHistory, session, subReasoning, ct)
                        .ConfigureAwait(false);
                    collected[sub.Order] = SubTaskOrchestrator.FormatSubTaskResult(sub, subResp);
                    runningHistory.Add(new ConversationMessage(ConversationRoles.User, subQuery));
                    runningHistory.Add(new ConversationMessage(ConversationRoles.Assistant, subResp));
                }
            }
        }

        return SubTaskOrchestrator.AggregateSubTaskResults(collected.Values.ToList());
    }

    public async IAsyncEnumerable<StreamEvent> RunDecomposedStreamingAsync(
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

        // Alt görev sonuçları TAMAMLANDIKÇA yayınlanır; kullanıcı hepsinin bitmesini beklemez.
        // Bu bayrak yalnızca ilk parçadan sonra ayırıcı koymak için.
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
                        var resp = await _runner.RunAsync(subQuery, historySnapshot, session, subReasoning, ct)
                            .ConfigureAwait(false);
                        var hints = _uiHint.DrainPending(sessionId);
                        return (sub, resp, hints);
                    }
                    finally { sem.Release(); }
                }).ToList();

                var results = await Task.WhenAll(tasks).ConfigureAwait(false);

                foreach (var (sub, resp, hints) in results.OrderBy(t => t.sub.Order))
                {
                    var part = SubTaskOrchestrator.FormatSubTaskResult(sub, resp);
                    collected[sub.Order] = part;
                    runningHistory.Add(new ConversationMessage(ConversationRoles.User,
                        SubTaskOrchestrator.FormatSubTaskQuery(sub)));
                    runningHistory.Add(new ConversationMessage(ConversationRoles.Assistant, resp));

                    foreach (var hint in hints)
                        yield return hint;

                    yield return new StreamEvent(StreamEventTypes.Agent,
                        new { name = $"SubTask#{sub.Order}", status = "done", order = sub.Order });

                    // Sonuç HAZIR olduğu anda yayınla — kalan alt görevler beklenmez.
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

                    _approvalContext.SetCurrentAgent(sub.TargetAgent);
                    await foreach (var evt in _runner.RunStreamingAsync(
                        subQuery, runningHistory, session, subReasoning, ct))
                    {
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
                    var part = SubTaskOrchestrator.FormatSubTaskResult(sub, subResponse);
                    collected[sub.Order] = part;
                    runningHistory.Add(new ConversationMessage(ConversationRoles.User, subQuery));
                    runningHistory.Add(new ConversationMessage(ConversationRoles.Assistant, subResponse));

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
        yield return new StreamEvent(StreamEventTypes.ResponseComplete,
            new ResponseCompletePayload(
                aggregated,
                TerminationReason: WellKnown.Termination.ReasonCompleted,
                Revised: false,
                Decomposed: true,
                SubTaskCount: total));
    }
}

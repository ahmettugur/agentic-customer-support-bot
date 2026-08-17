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
    private readonly WorkflowRunner _runner;
    private readonly ParallelExecutionOptions _parallelOptions;
    private readonly IUiHintEmitter _uiHint;
    private readonly IApprovalContextAccessor _approvalContext;

    public DecomposedRunner(
        WorkflowRunner runner,
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
                    await foreach (var evt in _runner.RunStreamingAsync(
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

        foreach (var chunk in WorkflowResponseExtractor.SplitIntoDeltaChunks(aggregated))
        {
            yield return new StreamEvent(StreamEventTypes.ResponseDelta, new TextDeltaPayload(chunk));
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
}

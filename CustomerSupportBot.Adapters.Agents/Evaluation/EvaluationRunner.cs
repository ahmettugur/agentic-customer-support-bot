// Adapters.Agents/Evaluation/EvaluationRunner.cs
// Evaluation-scenarios'daki senaryoları sistem üzerinde otomatik çalıştırır.
// Her senaryo için reasoning + workflow + critique akışını koşturur, trace üzerinden doğrular.
//
// MAF EvalItem/ChatMessage/FunctionCallContent tiplerini CriteriaEvaluator'a beslemek için
// kullandığından Adapters.Agents'ta yaşıyor (bkz. CriteriaEvaluator.cs başındaki not).

using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Ports.Inbound;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Adapters.Agents.Evaluation;

public class EvaluationRunner : IEvaluationPort
{
    private readonly IAgentTeamPort _team;
    private readonly IReasoningPort _reasoningService;
    private readonly ISessionManager _sessionManager;
    private readonly IReasoningTraceStore _traceStore;

    public EvaluationRunner(
        IAgentTeamPort team,
        IReasoningPort reasoningService,
        ISessionManager sessionManager,
        IReasoningTraceStore traceStore)
    {
        _team = team;
        _reasoningService = reasoningService;
        _sessionManager = sessionManager;
        _traceStore = traceStore;
    }

    /// <summary>
    /// Verilen senaryoları sıralı olarak çalıştırır ve ayrı session'lar kullanır.
    /// </summary>
    public async Task<EvaluationRunResult> RunAsync(
        List<EvaluationScenario> scenarios,
        CancellationToken ct = default)
    {
        var runResult = new EvaluationRunResult
        {
            TotalScenarios = scenarios.Count
        };

        foreach (var scenario in scenarios)
        {
            if (ct.IsCancellationRequested) break;
            var result = await RunScenarioAsync(scenario, ct);
            runResult.Results.Add(result);

            if (result.Passed) runResult.PassedScenarios++;
            else if (result.PassedCriteria == 0) runResult.FailedScenarios++;
            else runResult.PartialScenarios++;
        }

        runResult.CompletedAt = DateTime.UtcNow;
        return runResult;
    }

    /// <summary>Tek bir senaryoyu çalıştırır.</summary>
    public async Task<ScenarioResult> RunScenarioAsync(
        EvaluationScenario scenario,
        CancellationToken ct = default)
    {
        var result = new ScenarioResult
        {
            ScenarioId = scenario.Id,
            Category = scenario.Category,
            Query = scenario.Query
        };

        var startTime = DateTime.UtcNow;

        try
        {
            // İzole session — her senaryo kendi bağlamında çalışsın
            var session = _sessionManager.GetOrCreate(null);

            // Reasoning'i ÖNCE çalıştır ki PlanningAgent ön-analiz bağlamını alsın.
            // Senaryo tek-turlu çalışır, history boş; imzada named arg ile cancellation
            // Token'ı doğru slot'a bağlıyoruz.
            var reasoning = await _reasoningService.ReasonAsync(
                scenario.Query, session, history: null, ct: ct);
            var response = await _team.RunAsync(scenario.Query, null, session, reasoning, ct);

            result.Response = response;
            result.DetectedIntent = reasoning.Intent;

            // En son trace'i session'dan al (bu senaryoya ait olan)
            var trace = _traceStore.GetBySession(session.SessionId).LastOrDefault();
            result.TraceId = trace?.TraceId;
            result.TerminationReason = trace?.TerminationReason;
            result.AgentsVisited = trace?.AgentVisits
                .Select(v => SimplifyAgentName(v.AgentName))
                .Distinct()
                .ToList() ?? new();

            // Tool çağrılarını trace'in gerçek ToolCalls listesinden al (agent adından
            // tahmin etmek yerine — ReasoningTrace.ToolCalls zaten doğru ToolName'i tutuyor).
            var toolsCalled = (trace?.ToolCalls ?? new()).Select(tc => tc.ToolName).ToList();
            result.ToolsCalled = toolsCalled;

            // Success criteria evaluation
            var ctx = new ScenarioRunContext
            {
                Response = response,
                TerminationReason = trace?.TerminationReason,
                DetectedIntent = reasoning.Intent,
                IterationCount = trace?.IterationCount ?? 0,
                ToolsCalled = toolsCalled,
                AgentsVisited = result.AgentsVisited,
                ExpectedTools = scenario.ExpectedTools,
                SpecialistReasonings = trace?.SpecialistReasonings ?? new(),
                Reasoning = trace?.Reasoning,
                Planning = trace?.Planning
            };

            // EvalChecks gibi built-in MAF check'lerinin item.Conversation üzerinden
            // FunctionCallContent taraması yapabilmesi için sentetik bir konuşma kuruyoruz —
            // gerçek ChatMessage geçmişi trace'te tutulmuyor, ama tool adları biliniyor.
            var conversation = new List<ChatMessage> { new(ChatRole.User, scenario.Query) };
            conversation.AddRange(toolsCalled.Select(toolName =>
                new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(Guid.NewGuid().ToString(), toolName)])));
            var evalItem = new EvalItem(scenario.Query, response ?? "", conversation)
            {
                ExpectedToolCalls = scenario.ExpectedToolCalls.Count == 0
                    ? null
                    : scenario.ExpectedToolCalls
                        .Select(t => new ExpectedToolCall(t.Name, t.Arguments))
                        .ToList()
            };

            foreach (var criterion in scenario.SuccessCriteria)
            {
                var critResult = CriteriaEvaluator.Evaluate(criterion, evalItem, ctx);
                result.CriteriaResults.Add(critResult);
                if (critResult.Passed) result.PassedCriteria++;
            }

            // Ayrıca expected_intent ve expected_agents kontrolleri
            if (!string.IsNullOrEmpty(scenario.ExpectedIntent))
            {
                var intentMatch = reasoning.Intent?.Contains(scenario.ExpectedIntent,
                    StringComparison.OrdinalIgnoreCase) ?? false;
                result.CriteriaResults.Add(new CriterionResult
                {
                    Criterion = $"expected_intent: {scenario.ExpectedIntent}",
                    Passed = intentMatch,
                    Evaluation = intentMatch
                        ? $"intent '{reasoning.Intent}' eşleşti"
                        : $"intent '{reasoning.Intent}' beklenenle ('{scenario.ExpectedIntent}') eşleşmedi"
                });
                if (intentMatch) result.PassedCriteria++;
            }

            result.TotalCriteria = result.CriteriaResults.Count;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        result.DurationMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
        return result;
    }

    private static string SimplifyAgentName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "-";
        var idx = name.IndexOf('_');
        return idx > 0 ? name.Substring(0, idx) : name;
    }
}

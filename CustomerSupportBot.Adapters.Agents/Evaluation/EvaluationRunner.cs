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
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.Options;
using ChatResponse = Microsoft.Extensions.AI.ChatResponse;

namespace CustomerSupportBot.Adapters.Agents.Evaluation;

public class EvaluationRunner : IEvaluationPort
{
    private readonly IAgentTeamPort _team;
    private readonly IReasoningPort _reasoningService;
    private readonly ISessionManager _sessionManager;
    private readonly IReasoningTraceStore _traceStore;
    private readonly IChatClient _chatClient;
    private readonly EvaluationQualityOptions _qualityOptions;

    public EvaluationRunner(
        IAgentTeamPort team,
        IReasoningPort reasoningService,
        ISessionManager sessionManager,
        IReasoningTraceStore traceStore,
        IChatClient chatClient,
        IOptions<EvaluationQualityOptions> qualityOptions)
    {
        _team = team;
        _reasoningService = reasoningService;
        _sessionManager = sessionManager;
        _traceStore = traceStore;
        _chatClient = chatClient;
        _qualityOptions = qualityOptions.Value;
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

    /// <summary>
    /// Tek bir senaryoyu çalıştırır. <see cref="EvaluationScenario.Repetitions"/> 1'den büyükse
    /// senaryo N kez ayrı ayrı koşturulur (non-determinism ölçümü) ve ilk koşunun sonucu, tüm
    /// koşuların pass/fail dağılımıyla (<see cref="ScenarioResult.RepetitionOutcomes"/>/
    /// <see cref="ScenarioResult.RepetitionPassRate"/>) zenginleştirilerek döndürülür.
    /// </summary>
    public async Task<ScenarioResult> RunScenarioAsync(
        EvaluationScenario scenario,
        CancellationToken ct = default)
    {
        if (scenario.Repetitions <= 1)
            return await RunSingleAsync(scenario, ct);

        var runs = new List<ScenarioResult>();
        for (var i = 0; i < scenario.Repetitions; i++)
        {
            if (ct.IsCancellationRequested) break;
            runs.Add(await RunSingleAsync(scenario, ct));
        }

        return AggregateRepetitions(runs);
    }

    /// <summary>
    /// N koşunun sonuçlarını tek bir <see cref="ScenarioResult"/>'a indirger: ilk koşunun
    /// tüm alanları (Response, CriteriaResults vb.) korunur, üstüne Repetitions/RepetitionOutcomes/
    /// RepetitionPassRate eklenir. Saf/deterministik — LLM veya I/O gerektirmez, ayrı test edilebilir.
    /// </summary>
    internal static ScenarioResult AggregateRepetitions(List<ScenarioResult> runs)
    {
        var primary = runs[0];
        primary.Repetitions = runs.Count;
        primary.RepetitionOutcomes = runs.Select(r => r.Passed).ToList();
        primary.RepetitionPassRate = runs.Count == 0 ? 0.0 : runs.Count(r => r.Passed) / (double)runs.Count;
        return primary;
    }

    /// <summary>Senaryonun TEK bir koşusu — eski (repetitions öncesi) davranışın kendisi.</summary>
    private async Task<ScenarioResult> RunSingleAsync(
        EvaluationScenario scenario,
        CancellationToken ct)
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
            var session = await _sessionManager.GetOrCreateAsync(null, ct);

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

            if (scenario.QualityChecks.Count > 0)
            {
                foreach (var qc in scenario.QualityChecks)
                {
                    var qcResult = await RunQualityCheckAsync(qc, scenario.Query, response, ct);
                    result.CriteriaResults.Add(qcResult);
                    if (qcResult.Passed) result.PassedCriteria++;
                }
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

    /// <summary>
    /// MEAI LLM-judge kalite değerlendiricisini (relevance/coherence) çalıştırır.
    /// <see cref="EvaluationQualityOptions.Enabled"/> false ise (varsayılan) gerçek LLM çağrısı
    /// yapmadan <c>Skipped="quality_checks_disabled"</c> döner — her koşum ek bir judge-model
    /// çağrısı gerektirdiği için maliyetli, opt-in bir özellik.
    /// </summary>
    internal async Task<CriterionResult> RunQualityCheckAsync(
        string checkName, string query, string? response, CancellationToken ct)
    {
        var criterionLabel = $"quality_{checkName}";

        if (!_qualityOptions.Enabled)
        {
            return new CriterionResult
            {
                Criterion = criterionLabel,
                Passed = false,
                Skipped = "quality_checks_disabled",
                Evaluation = "EvaluationQuality:Enabled=false (appsettings.json) — LLM-judge çağrısı atlandı"
            };
        }

        IEvaluator evaluator = checkName.ToLowerInvariant() switch
        {
            "relevance" => new RelevanceEvaluator(),
            "coherence" => new CoherenceEvaluator(),
            _ => throw new ArgumentOutOfRangeException(nameof(checkName), checkName,
                "Desteklenen quality check'ler: relevance, coherence")
        };

        var chatConfig = new ChatConfiguration(_chatClient);
        var messages = new[] { new ChatMessage(ChatRole.User, query) };
        var modelResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, response ?? ""));

        var evalResult = await evaluator.EvaluateAsync(messages, modelResponse, chatConfig, cancellationToken: ct);
        var metric = evalResult.Metrics.Values.OfType<NumericMetric>().FirstOrDefault();

        if (metric is null)
        {
            return new CriterionResult
            {
                Criterion = criterionLabel,
                Passed = false,
                Skipped = "manual_review_needed",
                Evaluation = "MEAI evaluator metrik döndürmedi (diagnostics için trace'e bakın)"
            };
        }

        var failed = metric.Interpretation?.Failed ?? true;
        return new CriterionResult
        {
            Criterion = criterionLabel,
            Passed = !failed,
            Evaluation = $"score={metric.Value}, rating={metric.Interpretation?.Rating}, reason={metric.Reason}"
        };
    }

    private static string SimplifyAgentName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "-";
        var idx = name.IndexOf('_');
        return idx > 0 ? name.Substring(0, idx) : name;
    }
}

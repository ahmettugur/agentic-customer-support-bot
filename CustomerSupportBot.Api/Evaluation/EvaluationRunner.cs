// Evaluation/EvaluationRunner.cs
// Evaluation-scenarios.yaml'daki senaryoları sistem üzerinde otomatik çalıştırır.
// Her senaryo için reasoning + workflow + critique akışını koşturur, trace üzerinden doğrular.

using CustomerSupportBot.Api.Agents;
using CustomerSupportBot.Api.Services;
using CustomerSupportBot.Api.Models;
using Microsoft.Extensions.AI;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CustomerSupportBot.Api.Evaluation;

public class EvaluationRunner
{
    private readonly CustomerSupportTeam _team;
    private readonly ReasoningService _reasoningService;
    private readonly ISessionManager _sessionManager;
    private readonly IReasoningTraceStore _traceStore;

    public EvaluationRunner(
        CustomerSupportTeam team,
        ReasoningService reasoningService,
        ISessionManager sessionManager,
        IReasoningTraceStore traceStore)
    {
        _team = team;
        _reasoningService = reasoningService;
        _sessionManager = sessionManager;
        _traceStore = traceStore;
    }

    /// <summary>YAML dosyasından senaryoları yükler.</summary>
    public static ScenarioFile LoadScenarios(string yamlPath)
    {
        var yaml = File.ReadAllText(yamlPath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        return deserializer.Deserialize<ScenarioFile>(yaml);
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
            var session = _sessionManager.GetOrCreateSession(null);

            // Reasoning'i ÖNCE çalıştır ki PlanningAgent ön-analiz bağlamını alsın.
            // Senaryo tek-turlu çalışır, history boş; imzada named arg ile cancellation
            // Token'ı doğru slot'a bağlıyoruz.
            var reasoning = await _reasoningService.ReasonAsync(
                scenario.Query, session, history: null, cancellationToken: ct);
            var response = await _team.RunAsync(scenario.Query, null, session, reasoning);

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

            // Tool çağrılarını specialist reasoning'lerden derle
            var toolsCalled = new List<string>();
            foreach (var sp in trace?.SpecialistReasonings ?? new())
            {
                if (sp.ResultConfidence.HasValue && sp.PreToolCheck?.CanProceed == true)
                {
                    // Hangi tool çağrıldı bilinmiyor — agent adından türet
                    toolsCalled.Add(AgentToToolName(sp.AgentName));
                }
            }
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

            foreach (var criterion in scenario.SuccessCriteria)
            {
                var critResult = CriteriaEvaluator.Evaluate(criterion, ctx);
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

    private static string AgentToToolName(string agentName)
    {
        var simple = SimplifyAgentName(agentName);
        return simple switch
        {
            "ProductInquiryAgent" => "product_inquiry_tool",
            "OrderPlacementAgent" => "order_placement_tool",
            "OrderInquiryAgent" => "order_status_tool", // Birden fazla tool var, ilkini say
            "ComplaintAgent" => "complaint_registration_tool",
            _ => simple.ToLowerInvariant()
        };
    }
}

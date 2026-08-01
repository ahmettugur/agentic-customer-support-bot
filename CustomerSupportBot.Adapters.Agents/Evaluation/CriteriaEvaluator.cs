// Adapters.Agents/Evaluation/CriteriaEvaluator.cs
// docs/evaluation-scenarios.yaml'daki yapılandırılmış (typed) success_criteria'ları
// Microsoft.Agents.AI'ın gerçek EvalCheck/EvalItem/EvalCheckResult tipleri üzerinden
// değerlendirir. Desteklenen type'lar: contains_any, tool_called, tool_not_called,
// turn_count, iteration_count, no_extra_tool_calls, no_missing_param_tool,
// agent_requests_field, complaint_id_returned, order_id_returned, customer_id_used,
// order_status_contains, manual_review. Bilinmeyen type ve manual_review aynı şekilde
// "manual_review_needed" olarak işaretlenir.
//
// Bu sınıf MAF (Microsoft.Agents.AI) tiplerine bağımlı olduğu için Adapters.Agents'ta
// yaşıyor — CustomerSupportBot.Application projesi kasıtlı olarak sadece Domain'e bağımlı
// (bkz. Application.csproj), framework-agnostic kalmak zorunda.

using System.Text.RegularExpressions;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Domain.Model;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Adapters.Agents.Evaluation;

public static class CriteriaEvaluator
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(500);

    private static readonly Dictionary<string, Func<CriterionSpec, ScenarioRunContext, EvalCheck>> Checks = new()
    {
        ["contains_any"] = (spec, _) => FunctionEvaluator.Create("contains_any",
            (string response) => (spec.Values ?? []).Any(v =>
                response.Contains(v, StringComparison.OrdinalIgnoreCase))),

        ["tool_called"] = (spec, _) =>
            EvalChecks.ToolCalledCheck(ToolCalledMode.All, (spec.Values ?? []).ToArray()),

        ["tool_not_called"] = (spec, _) => FunctionEvaluator.Create("tool_not_called", (EvalItem item) =>
        {
            var called = CalledToolNames(item);
            var offending = (spec.Values ?? []).Where(t => called.Contains(t)).ToList();
            var passed = offending.Count == 0;
            return new EvalCheckResult(passed,
                passed ? "hiçbiri çağrılmadı" : $"çağrılmamalıydı: {string.Join(", ", offending)}",
                "tool_not_called");
        }),

        // Gerçek built-in — item.ExpectedToolCalls'a göre isim+argüman (subset) eşleşmesi yapar.
        // EvaluationRunner, scenario.ExpectedToolCalls'ı EvalItem.ExpectedToolCalls'a taşır.
        ["tool_call_args_match"] = (_, _) => EvalChecks.ToolCallArgsMatch(),

        ["turn_count"] = (spec, ctx) => FunctionEvaluator.Create("turn_count",
            (EvalItem _) => CompareResult("turn_count", ctx.IterationCount, spec.Op, spec.Value)),

        ["iteration_count"] = (spec, ctx) => FunctionEvaluator.Create("iteration_count",
            (EvalItem _) => CompareResult("iteration_count", ctx.IterationCount, spec.Op, spec.Value)),

        ["no_extra_tool_calls"] = (_, ctx) => FunctionEvaluator.Create("no_extra_tool_calls", (EvalItem _) =>
        {
            var expected = ctx.ExpectedTools.Count;
            var actual = ctx.ToolsCalled.Count;
            return new EvalCheckResult(actual <= expected,
                $"beklenen tool sayısı {expected}, çağrılan {actual}", "no_extra_tool_calls");
        }),

        ["no_missing_param_tool"] = (_, ctx) => FunctionEvaluator.Create("no_missing_param_tool", (EvalItem _) =>
        {
            var hasValidationError = ctx.SpecialistReasonings.Any(s =>
                s.ResultConfidence == 0.0 && s.PreToolCheck?.CanProceed == true);
            return new EvalCheckResult(!hasValidationError,
                hasValidationError ? "tool validation hatası tespit edildi" : "tool validation hatası yok",
                "no_missing_param_tool");
        }),

        ["agent_requests_field"] = (_, ctx) => FunctionEvaluator.Create("agent_requests_field", (EvalItem _) =>
        {
            var resp = (ctx.Response ?? "").ToLowerInvariant();
            var asksForInfo = resp.Contains("müşteri kimlik") || resp.Contains("sipariş numara")
                              || resp.Contains("customer_id") || resp.Contains("order_id")
                              || ctx.TerminationReason == "awaiting_user_input";
            return new EvalCheckResult(asksForInfo,
                asksForInfo ? "yanıtta ek bilgi isteği tespit edildi" : "yanıtta bilgi isteği bulunmadı",
                "agent_requests_field");
        }),

        ["complaint_id_returned"] = (_, ctx) => FunctionEvaluator.Create("complaint_id_returned", (EvalItem _) =>
        {
            var hasId = SafeIsMatch(ctx.Response, @"\bşikayet.*?\d{4,}|\d{4,}.*?şikayet|\b\d{4,}\b");
            return new EvalCheckResult(hasId, hasId ? "şikayet no bulundu" : "şikayet no yanıtta bulunmadı",
                "complaint_id_returned");
        }),

        ["order_id_returned"] = (_, ctx) => FunctionEvaluator.Create("order_id_returned", (EvalItem _) =>
        {
            var hasId = SafeIsMatch(ctx.Response, @"\b\d{4,}\b");
            return new EvalCheckResult(hasId, hasId ? "sipariş no bulundu" : "sipariş no yanıtta bulunmadı",
                "order_id_returned");
        }),

        ["customer_id_used"] = (_, ctx) => FunctionEvaluator.Create("customer_id_used", (EvalItem _) =>
        {
            var usedCorrectly = ctx.SpecialistReasonings.Any(s =>
                s.PreToolCheck?.CollectedParams.Any(p =>
                    p.Contains("customer_id", StringComparison.OrdinalIgnoreCase)) == true);
            return new EvalCheckResult(usedCorrectly,
                usedCorrectly ? "customer_id tool'a geçildi" : "customer_id kullanımı tespit edilmedi",
                "customer_id_used");
        }),

        ["order_status_contains"] = (_, _) => FunctionEvaluator.Create("order_status_contains",
            (string response) =>
            {
                var resp = response.ToLowerInvariant();
                return resp.Contains("durum") || resp.Contains("teslim") || resp.Contains("işleniyor")
                       || resp.Contains("kargo");
            }),

        ["manual_review"] = (spec, _) => (EvalItem _) =>
            new EvalCheckResult(false, spec.Note ?? "otomatik değerlendirme desteklenmiyor", "manual_review"),
    };

    public static CriterionResult Evaluate(CriterionSpec spec, EvalItem item, ScenarioRunContext ctx)
    {
        if (!Checks.TryGetValue(spec.Type, out var factory))
        {
            return new CriterionResult
            {
                Criterion = DescribeCriterion(spec),
                Passed = false,
                Skipped = "manual_review_needed",
                Evaluation = $"bilinmeyen kriter tipi: {spec.Type}"
            };
        }

        var result = factory(spec, ctx)(item);
        return new CriterionResult
        {
            Criterion = DescribeCriterion(spec),
            Passed = result.Passed,
            Evaluation = result.Reason,
            Skipped = spec.Type == "manual_review" ? "manual_review_needed" : null
        };
    }

    private static string DescribeCriterion(CriterionSpec spec) => spec.Type switch
    {
        "manual_review" => $"manual_review: {spec.Note}",
        "turn_count" or "iteration_count" => $"{spec.Type} {spec.Op} {spec.Value}",
        "contains_any" => $"contains_any: {string.Join(" OR ", spec.Values ?? [])}",
        "tool_called" or "tool_not_called" => $"{spec.Type}: {string.Join(", ", spec.Values ?? [])}",
        "agent_requests_field" => $"agent_requests_field: {spec.Field}",
        _ => spec.Type
    };

    private static EvalCheckResult CompareResult(string checkName, int actual, string? op, int? value)
    {
        var n = value ?? 0;
        var pass = op switch
        {
            "<=" => actual <= n,
            "<" => actual < n,
            ">=" => actual >= n,
            ">" => actual > n,
            "==" or "=" => actual == n,
            _ => false
        };
        return new EvalCheckResult(pass, $"{checkName}={actual}, beklenen {op} {n}", checkName);
    }

    private static HashSet<string> CalledToolNames(EvalItem item)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var content in item.Conversation.SelectMany(m => m.Contents).OfType<FunctionCallContent>())
        {
            names.Add(content.Name);
        }
        return names;
    }

    private static bool SafeIsMatch(string? input, string pattern)
    {
        try
        {
            return Regex.IsMatch(input ?? "", pattern, RegexOptions.IgnoreCase, RegexTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}

/// <summary>Senaryo çalıştırma bağlamı — CriteriaEvaluator'a iletilir.</summary>
public class ScenarioRunContext
{
    public string? Response { get; set; }
    public string? TerminationReason { get; set; }
    public string? DetectedIntent { get; set; }
    public int IterationCount { get; set; }
    public List<string> ToolsCalled { get; set; } = new();
    public List<string> AgentsVisited { get; set; } = new();
    public List<string> ExpectedTools { get; set; } = new();
    public List<SpecialistReasoning> SpecialistReasonings { get; set; } = new();
    public ReasoningResult? Reasoning { get; set; }
    public PlanningResult? Planning { get; set; }
}

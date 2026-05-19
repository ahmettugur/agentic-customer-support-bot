// Application/Services/Evaluation/CriteriaEvaluator.cs
// YAML'daki success_criteria metinlerini best-effort regex ile değerlendirir.
// Desteklenen pattern'lar:
//   - "response contains '<text>'" veya "response contains <text>"
//   - "response contains <a> OR <b>"
//   - "turn_count <= N" | "turn_count == N" | "turn_count < N"
//   - "<tool_name> called"
//   - "<tool_name> NOT called"
//   - "no extra tool calls"
//   - "no missing_param_tool error"
//   - "agent requests <field>" (response asks for field)
//   - "complaint id returned" / "order id returned" (regex ORD-\d / CMP-\d)
// Diğer pattern'lar → "manual_review" olarak işaretlenir.

using System.Text.RegularExpressions;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Evaluation;

public static class CriteriaEvaluator
{
    public static CriterionResult Evaluate(string criterion, ScenarioRunContext ctx)
    {
        var c = criterion.Trim();
        var lower = c.ToLowerInvariant();

        // ─── response contains ... ───
        if (lower.StartsWith("response contains"))
        {
            var after = c.Substring("response contains".Length).Trim();
            return EvalContains(c, after, ctx.Response);
        }

        // ─── turn_count ≤/</= ... ───
        var turnMatch = Regex.Match(lower, @"turn_count\s*(<=|<|==|>=|>|=)\s*(\d+)");
        if (turnMatch.Success)
        {
            var op = turnMatch.Groups[1].Value;
            var n = int.Parse(turnMatch.Groups[2].Value);
            // Bir "turn" = kullanıcı mesajı (tek turn tek senaryoda)
            var actual = ctx.IterationCount;
            var pass = op switch
            {
                "<=" => actual <= n,
                "<" => actual < n,
                ">=" => actual >= n,
                ">" => actual > n,
                "==" or "=" => actual == n,
                _ => false
            };
            return new CriterionResult
            {
                Criterion = c,
                Passed = pass,
                Evaluation = $"iteration_count={actual}, beklenen {op} {n}"
            };
        }

        // ─── no extra tool calls ───
        if (lower.Contains("no extra tool call"))
        {
            var expected = ctx.ExpectedTools.Count;
            var actual = ctx.ToolsCalled.Count;
            return new CriterionResult
            {
                Criterion = c,
                Passed = actual <= expected,
                Evaluation = $"beklenen tool sayısı {expected}, çağrılan {actual}"
            };
        }

        // ─── no missing_param_tool error ───
        // Tool validation hatası almadıysak true
        if (lower.Contains("no missing_param_tool"))
        {
            var hasValidationError = ctx.SpecialistReasonings.Any(s =>
                s.ResultConfidence == 0.0
                && s.PreToolCheck?.CanProceed == true); // Ama tool validation döndü
            return new CriterionResult
            {
                Criterion = c,
                Passed = !hasValidationError,
                Evaluation = hasValidationError ? "tool validation hatası tespit edildi" : "tool validation hatası yok"
            };
        }

        // ─── <tool_name> NOT called ───
        var notCalledMatch = Regex.Match(lower, @"(\w+_tool)\s+not\s+called");
        if (notCalledMatch.Success)
        {
            var toolName = notCalledMatch.Groups[1].Value;
            var called = ctx.ToolsCalled.Any(t => t.Contains(toolName, StringComparison.OrdinalIgnoreCase));
            return new CriterionResult
            {
                Criterion = c,
                Passed = !called,
                Evaluation = called ? $"{toolName} çağrıldı (beklenmiyordu)" : $"{toolName} çağrılmadı"
            };
        }

        // ─── <tool_name> called ───
        var calledMatch = Regex.Match(lower, @"(\w+_tool)\s+called");
        if (calledMatch.Success)
        {
            var toolName = calledMatch.Groups[1].Value;
            var called = ctx.ToolsCalled.Any(t => t.Contains(toolName, StringComparison.OrdinalIgnoreCase));
            return new CriterionResult
            {
                Criterion = c,
                Passed = called,
                Evaluation = called ? $"{toolName} çağrıldı" : $"{toolName} çağrılmadı"
            };
        }

        // ─── agent requests <field> ───
        if (lower.Contains("agent requests") || lower.Contains("requests customer_id")
            || lower.Contains("requests order_id") || lower.Contains("customer_id requested"))
        {
            // Response bir clarification sorusu mu? "customer_id" veya "müşteri" gibi ifade içeriyor mu?
            var resp = (ctx.Response ?? "").ToLowerInvariant();
            var asksForInfo = resp.Contains("müşteri kimlik") || resp.Contains("sipariş numara")
                              || resp.Contains("customer_id") || resp.Contains("order_id")
                              || ctx.TerminationReason == "awaiting_user_input";
            return new CriterionResult
            {
                Criterion = c,
                Passed = asksForInfo,
                Evaluation = asksForInfo ? "yanıtta ek bilgi isteği tespit edildi" : "yanıtta bilgi isteği bulunmadı"
            };
        }

        // ─── complaint/order id returned ───
        if (lower.Contains("complaint id") || lower.Contains("şikayet no"))
        {
            var hasId = Regex.IsMatch(ctx.Response ?? "", @"\bCMP[-_]?\d+\b|\bşikayet.*?\d+", RegexOptions.IgnoreCase);
            return new CriterionResult
            {
                Criterion = c,
                Passed = hasId,
                Evaluation = hasId ? "şikayet no bulundu" : "şikayet no yanıtta bulunmadı"
            };
        }

        if (lower.Contains("order id"))
        {
            var hasId = Regex.IsMatch(ctx.Response ?? "", @"\bORD[-_]?\d+\b", RegexOptions.IgnoreCase);
            return new CriterionResult
            {
                Criterion = c,
                Passed = hasId,
                Evaluation = hasId ? "sipariş no bulundu" : "sipariş no yanıtta bulunmadı"
            };
        }

        // ─── customer_id used correctly ───
        if (lower.Contains("customer_id used") || lower.Contains("uses customer_id"))
        {
            // Tool çağrılmış ve pre-check'te customer_id toplanmış mı?
            var usedCorrectly = ctx.SpecialistReasonings.Any(s =>
                s.PreToolCheck?.CollectedParams.Any(p =>
                    p.Contains("customer_id", StringComparison.OrdinalIgnoreCase)) == true);
            return new CriterionResult
            {
                Criterion = c,
                Passed = usedCorrectly,
                Evaluation = usedCorrectly ? "customer_id tool'a geçildi" : "customer_id kullanımı tespit edilmedi"
            };
        }

        // ─── response contains order status ───
        if (lower.Contains("order status") || lower.Contains("sipariş durumu") || lower.Contains("ordered list"))
        {
            var resp = (ctx.Response ?? "").ToLowerInvariant();
            var has = resp.Contains("durum") || resp.Contains("teslim") || resp.Contains("işleniyor")
                      || resp.Contains("kargo");
            return new CriterionResult
            {
                Criterion = c,
                Passed = has,
                Evaluation = has ? "yanıtta sipariş durumu bilgisi var" : "sipariş durumu bulunamadı"
            };
        }

        // ─── Eşleşmeyen kriter ───
        return new CriterionResult
        {
            Criterion = c,
            Passed = false,
            Skipped = "manual_review_needed",
            Evaluation = "otomatik değerlendirme desteklenmiyor"
        };
    }

    private static CriterionResult EvalContains(string criterion, string target, string? response)
    {
        var resp = response ?? "";
        var respLower = resp.ToLowerInvariant();

        // "A OR B OR C" formatı
        var parts = SplitByOr(target);
        foreach (var part in parts)
        {
            var cleaned = part.Trim().Trim('\'', '"');
            if (string.IsNullOrWhiteSpace(cleaned)) continue;
            if (respLower.Contains(cleaned.ToLowerInvariant()))
            {
                return new CriterionResult
                {
                    Criterion = criterion,
                    Passed = true,
                    Evaluation = $"'{cleaned}' yanıtta bulundu"
                };
            }
        }

        return new CriterionResult
        {
            Criterion = criterion,
            Passed = false,
            Evaluation = $"hiçbir eşleşme bulunamadı: {string.Join(" / ", parts)}"
        };
    }

    private static List<string> SplitByOr(string text)
    {
        return text.Split(new[] { " OR ", " or " }, StringSplitOptions.RemoveEmptyEntries)
                   .Select(s => s.Trim())
                   .ToList();
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

// Services/ReasoningSanityChecker.cs
// Deterministic kural tabanlı sanity checker.
// Reasoning LLM'in ürettiği ReasoningResult'u VerifiedEntities ile karşılaştırır
// ve mantık tutarsızlıklarını yakalar. Hiçbir LLM çağrısı yapmaz — saf kurallar.
//
// Mimari: Strategy pattern — her kural ayrı bir IReasoningSanityRule.
// Yeni kural eklemek için sadece sınıf yaz ve _rules listesine ekle.

using CustomerSupportBot.Api.Models;

namespace CustomerSupportBot.Api.Services;

/// <summary>
/// Tek bir sanity kuralının arayüzü. Reasoning + verified entities üzerinde çalışır,
/// bulduğu issue'ları parametre listesine ekler.
/// </summary>
public interface IReasoningSanityRule
{
    /// <summary>Kural kodu (issue.Code ile aynı). Logging ve test için kullanılır.</summary>
    string Code { get; }

    /// <summary>
    /// Kuralı uygular. Issue tespit edilirse <paramref name="issues"/> listesine eklenir.
    /// </summary>
    void Apply(ReasoningResult result, VerifiedEntities verified, List<ReasoningIssue> issues);
}

/// <summary>
/// ReasoningResult üzerinde deterministic kural tabanlı tutarsızlık taraması yapar.
/// Tüm <see cref="IReasoningSanityRule"/> implementasyonlarını sırayla çalıştırır.
/// </summary>
public class ReasoningSanityChecker
{
    private readonly ILogger<ReasoningSanityChecker> _logger;
    private readonly IReadOnlyList<IReasoningSanityRule> _rules;

    public ReasoningSanityChecker(ILogger<ReasoningSanityChecker> logger)
    {
        _logger = logger;
        _rules =
        [
            new OverconfidentClarificationRule(),
            new RedundantRequiredInfoRule(),
            new IntentActionMismatchRule(),
            new LowConfidenceNoMissingRule(),
            new AssumptionHeavyStepsRule(),
            new OverconfidentAssumptionsRule(),
            new NotFoundIgnoredRule(),
            new SubTasksIgnoredRule(),
        ];
    }

    /// <summary>
    /// Verilen reasoning sonucunu + doğrulanmış entity bağlamını kontrol eder.
    /// Tüm kuralları uygular ve bulunan issue'ları liste olarak döner.
    /// </summary>
    public List<ReasoningIssue> Check(ReasoningResult result, VerifiedEntities verified)
    {
        var issues = new List<ReasoningIssue>();

        foreach (var rule in _rules)
        {
            try
            {
                rule.Apply(result, verified, issues);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Sanity rule {Rule} failed; atlanıyor", rule.Code);
            }
        }

        if (issues.Count > 0)
        {
            var errors = issues.Count(i => i.Severity == IssueSeverity.Error);
            var warns = issues.Count(i => i.Severity == IssueSeverity.Warn);
            _logger.LogInformation(
                "Sanity check: {Total} issue ({Errors} error, {Warns} warn)",
                issues.Count, errors, warns);

            foreach (var issue in issues.Where(i => i.Severity == IssueSeverity.Error))
            {
                _logger.LogWarning("Reasoning sanity ERROR — {Code}: {Message}",
                    issue.Code, issue.Message);
            }
        }

        return issues;
    }
}

// ════════════════════════════════════════════════════════════════
// KURALLAR
// ════════════════════════════════════════════════════════════════

/// <summary>Yüksek confidence + clarification çelişkisi.</summary>
public sealed class OverconfidentClarificationRule : IReasoningSanityRule
{
    public string Code => "overconfident_clarification";

    public void Apply(ReasoningResult r, VerifiedEntities _, List<ReasoningIssue> issues)
    {
        if (r.ConfidenceScore < 0.7) return;

        var action = r.NextAction?.ToLowerInvariant() ?? "";
        var isClarification =
            action.Contains("iste") ||
            action.Contains("sor") ||
            action.Contains("netleş") ||
            action.Contains("açıkla") ||
            action.Contains("clarif");

        if (isClarification)
        {
            issues.Add(new ReasoningIssue
            {
                Code = Code,
                Severity = IssueSeverity.Warn,
                Message = $"confidenceScore={r.ConfidenceScore:F2} yüksek ama nextAction " +
                          "'kullanıcıdan bilgi iste' formatında — tutarsız.",
                Field = "nextAction",
                SuggestedFix = "Ya confidenceScore'u 0.5-0.6 aralığına düşür, " +
                               "ya da nextAction'ı bir tool/agent çağrısına çevir."
            });
        }
    }
}

/// <summary>RequiredInfo'da DB-verified bir entity istemek (ping-pong tehlikesi).</summary>
public sealed class RedundantRequiredInfoRule : IReasoningSanityRule
{
    public string Code => "redundant_required_info";

    public void Apply(ReasoningResult r, VerifiedEntities verified, List<ReasoningIssue> issues)
    {
        if (r.RequiredInfo.Count == 0) return;

        var orderIdVerified = verified.OrderId?.Verification == EntityVerification.Verified;
        var customerIdVerified = verified.CustomerId?.Verification == EntityVerification.Verified;
        var complaintIdVerified = verified.ComplaintId?.Verification == EntityVerification.Verified;

        foreach (var (req, idx) in r.RequiredInfo.Select((x, i) => (x, i)))
        {
            var lower = req.ToLowerInvariant();
            string? which = null;

            if (orderIdVerified && (lower.Contains("sipariş") && lower.Contains("no") ||
                                     lower.Contains("sipariş_numara") ||
                                     lower.Contains("order_id") || lower.Contains("order id")))
            {
                which = WellKnown.ToolParameterNames.OrderId;
            }
            else if (customerIdVerified && (lower.Contains("müşteri") && lower.Contains("kim") ||
                                            lower.Contains("customer_id") || lower.Contains("customer id") ||
                                            lower.Contains("müşteri_kimli")))
            {
                which = WellKnown.ToolParameterNames.CustomerId;
            }
            else if (complaintIdVerified && (lower.Contains("şikayet") && lower.Contains("no") ||
                                             lower.Contains("complaint_id")))
            {
                which = "complaint_id";
            }

            if (which != null)
            {
                issues.Add(new ReasoningIssue
                {
                    Code = Code,
                    Severity = IssueSeverity.Error,
                    Message = $"requiredInfo[{idx}]='{req}' ama {which} zaten VERIFIED — " +
                              "bu alan sorulursa ping-pong olur.",
                    Field = $"requiredInfo[{idx}]",
                    SuggestedFix = $"requiredInfo'dan '{req}' kaldır; {which} zaten elinde."
                });
            }
        }
    }
}

/// <summary>Intent ile seçilen agent çelişiyor.</summary>
public sealed class IntentActionMismatchRule : IReasoningSanityRule
{
    public string Code => "intent_action_mismatch";

    public void Apply(ReasoningResult r, VerifiedEntities _, List<ReasoningIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(r.Intent) || string.IsNullOrWhiteSpace(r.NextAction)) return;

        var action = r.NextAction.ToLowerInvariant();
        var intent = r.Intent.ToLowerInvariant();

        var (mismatch, expected) = DetectMismatch(intent, action);
        if (!mismatch) return;

        issues.Add(new ReasoningIssue
        {
            Code = Code,
            Severity = IssueSeverity.Warn,
            Message = $"intent='{r.Intent}' ama nextAction='{r.NextAction}' — çelişkili.",
            Field = "nextAction",
            SuggestedFix = $"nextAction'ı {expected} ile başlat veya intent'i düzelt."
        });
    }

    private static (bool mismatch, string expected) DetectMismatch(string intent, string action)
    {
        if (intent.Contains(WellKnown.Intents.Complaint) &&
            (action.Contains("orderinquiry") || action.Contains("productinquiry") || action.Contains("orderplacement")))
            return (true, WellKnown.AgentNames.Complaint);

        if (intent.Contains(WellKnown.Intents.OrderCreation) &&
            (action.Contains("orderinquiry") || action.Contains("complaintagent")))
            return (true, WellKnown.AgentNames.Order);

        if (intent.Contains(WellKnown.Intents.OrderInquiry) &&
            (action.Contains("orderplacement") || action.Contains("complaintagent")))
            return (true, WellKnown.AgentNames.Order);

        if (intent.Contains(WellKnown.Intents.ProductInfo) &&
            (action.Contains("complaintagent") || action.Contains("orderinquiry")))
            return (true, WellKnown.AgentNames.ProductInquiry);

        return (false, "");
    }
}

/// <summary>Düşük confidence + boş requiredInfo: gizli belirsizlik.</summary>
public sealed class LowConfidenceNoMissingRule : IReasoningSanityRule
{
    public string Code => "low_confidence_no_missing";

    public void Apply(ReasoningResult r, VerifiedEntities _, List<ReasoningIssue> issues)
    {
        if (r.RequiredInfo.Count > 0) return;
        if (r.ConfidenceScore >= 0.5) return;

        issues.Add(new ReasoningIssue
        {
            Code = Code,
            Severity = IssueSeverity.Info,
            Message = $"confidenceScore={r.ConfidenceScore:F2} düşük ama requiredInfo boş — " +
                      "düşük güvenin nedeni gizli olabilir.",
            Field = "confidenceScore",
            SuggestedFix = "Assumptions veya rationale alanlarını kontrol et — neden düşük güven?"
        });
    }
}

/// <summary>Steps'te grounding=assumption olan adımlar.</summary>
public sealed class AssumptionHeavyStepsRule : IReasoningSanityRule
{
    public string Code => "assumption_based_step";

    public void Apply(ReasoningResult r, VerifiedEntities _, List<ReasoningIssue> issues)
    {
        var assumptionSteps = r.Steps
            .Where(s => string.Equals(s.Grounding, "assumption", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (assumptionSteps.Count == 0) return;

        foreach (var step in assumptionSteps)
        {
            issues.Add(new ReasoningIssue
            {
                Code = Code,
                Severity = IssueSeverity.Info,
                Message = $"steps[{step.Order}] grounding=assumption — " +
                          "kanıta değil varsayıma dayanıyor.",
                Field = $"steps[{step.Order}]",
                SuggestedFix = "Bu adımı gerçekleştirmeden önce session state, history veya " +
                               "DB'den doğrula."
            });
        }
    }
}

/// <summary>Yüksek confidence + çok varsayım = overconfident.</summary>
public sealed class OverconfidentAssumptionsRule : IReasoningSanityRule
{
    public string Code => "overconfident_assumptions";

    public void Apply(ReasoningResult r, VerifiedEntities _, List<ReasoningIssue> issues)
    {
        if (r.ConfidenceScore < 0.8) return;
        if (r.Assumptions.Count < 3) return;

        issues.Add(new ReasoningIssue
        {
            Code = Code,
            Severity = IssueSeverity.Warn,
            Message = $"confidenceScore={r.ConfidenceScore:F2} yüksek ama " +
                      $"{r.Assumptions.Count} varsayım var — overconfident.",
            Field = "confidenceScore",
            SuggestedFix = $"Her varsayım %5-10 belirsizlik katar; confidence'ı " +
                           $"{Math.Max(0.0, r.ConfidenceScore - 0.1 * r.Assumptions.Count):F2}'e düşür."
        });
    }
}

/// <summary>DB'de bulunamayan entity var ama nextAction doğrulamıyor.</summary>
public sealed class NotFoundIgnoredRule : IReasoningSanityRule
{
    public string Code => "not_found_ignored";

    public void Apply(ReasoningResult r, VerifiedEntities verified, List<ReasoningIssue> issues)
    {
        var notFound = new List<string>();
        if (verified.OrderId?.Verification == EntityVerification.NotFoundInDb)
            notFound.Add($"order_id={verified.OrderId.Value}");
        if (verified.ComplaintId?.Verification == EntityVerification.NotFoundInDb)
            notFound.Add($"complaint_id={verified.ComplaintId.Value}");

        if (notFound.Count == 0) return;

        var action = r.NextAction?.ToLowerInvariant() ?? "";
        var hasVerificationIntent =
            action.Contains("doğrulat") ||
            action.Contains("yanlış") ||
            action.Contains("kontrol") ||
            action.Contains("teyit");

        var mentionsInAssumptions = r.Assumptions.Any(a =>
            a.ToLowerInvariant().Contains("yanlış") ||
            a.ToLowerInvariant().Contains("bulunam"));

        if (!hasVerificationIntent && !mentionsInAssumptions)
        {
            issues.Add(new ReasoningIssue
            {
                Code = Code,
                Severity = IssueSeverity.Error,
                Message = $"Şu entity'ler DB'de bulunamadı: {string.Join(", ", notFound)}. " +
                          "Ama nextAction doğrulatma/kontrol içermiyor — hallucination riski.",
                Field = "nextAction",
                SuggestedFix = "nextAction'ı 'kullanıcıya <entity> numarasını doğrulat' olarak " +
                               "değiştir; confidence'ı da 0.5 civarına çek."
            });
        }
    }
}

/// <summary>Compound query var ama nextAction tek agent yönlendirmesi.</summary>
public sealed class SubTasksIgnoredRule : IReasoningSanityRule
{
    public string Code => "subtasks_ignored";

    public void Apply(ReasoningResult r, VerifiedEntities _, List<ReasoningIssue> issues)
    {
        if (r.SubTasks.Count < 2) return;

        var agents = r.SubTasks
            .Select(s => s.TargetAgent)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (agents.Count < 2) return;

        var action = r.NextAction?.ToLowerInvariant() ?? "";
        var mentionedAll = agents.All(a => action.Contains(a.ToLowerInvariant()));

        if (!mentionedAll)
        {
            var missing = agents.Where(a => !action.Contains(a.ToLowerInvariant())).ToList();
            issues.Add(new ReasoningIssue
            {
                Code = Code,
                Severity = IssueSeverity.Warn,
                Message = $"subTasks {r.SubTasks.Count} alt görev içeriyor ({string.Join(", ", agents)}) " +
                          $"ama nextAction sadece '{r.NextAction}' — {string.Join(", ", missing)} " +
                          "unutuluyor.",
                Field = "nextAction",
                SuggestedFix = "nextAction'a tüm alt görevleri sırayla ekle, ya da PlanningAgent'ın " +
                               "çoklu routing yapmasını sağla (ör. 'OrderInquiryAgent: ... / " +
                               "ComplaintAgent: ...')."
            });
        }
    }
}

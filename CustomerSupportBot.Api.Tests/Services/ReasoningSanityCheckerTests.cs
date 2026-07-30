// Tests/Services/ReasoningSanityCheckerTests.cs

using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Api.Tests.Services;

public class ReasoningSanityCheckerTests
{
    private readonly ReasoningSanityChecker _checker = new(NullLogger<ReasoningSanityChecker>.Instance);

    // ─── OverconfidentClarificationRule ───
    [Fact]
    public void OverconfidentClarification_HighConfidencePlusClarification_Warns()
    {
        var rule = new OverconfidentClarificationRule();
        var issues = new List<ReasoningIssue>();
        rule.Apply(new ReasoningResult
        {
            ConfidenceScore = 0.9,
            NextAction = "Kullanıcıdan customer_id iste"
        }, new VerifiedEntities(), issues);

        issues.Should().ContainSingle().Which.Code.Should().Be("overconfident_clarification");
    }

    [Fact]
    public void OverconfidentClarification_LowConfidence_NoIssue()
    {
        var rule = new OverconfidentClarificationRule();
        var issues = new List<ReasoningIssue>();
        rule.Apply(new ReasoningResult
        {
            ConfidenceScore = 0.4,
            NextAction = "Kullanıcıdan customer_id iste"
        }, new VerifiedEntities(), issues);

        issues.Should().BeEmpty();
    }

    // ─── RedundantRequiredInfoRule ───
    [Fact]
    public void RedundantRequiredInfo_VerifiedOrderInRequired_Errors()
    {
        var rule = new RedundantRequiredInfoRule();
        var issues = new List<ReasoningIssue>();
        var verified = new VerifiedEntities
        {
            OrderId = new VerifiedEntity
            {
                Value = "1030",
                Verification = EntityVerification.Verified
            }
        };
        rule.Apply(new ReasoningResult
        {
            RequiredInfo = new() { "order_id" }
        }, verified, issues);

        issues.Should().ContainSingle();
        issues[0].Severity.Should().Be(IssueSeverity.Error);
    }

    [Fact]
    public void RedundantRequiredInfo_NoVerified_NoIssue()
    {
        var rule = new RedundantRequiredInfoRule();
        var issues = new List<ReasoningIssue>();
        rule.Apply(new ReasoningResult
        {
            RequiredInfo = new() { "order_id" }
        }, new VerifiedEntities(), issues);
        issues.Should().BeEmpty();
    }

    // ─── IntentActionMismatchRule ───
    [Fact]
    public void IntentActionMismatch_ComplaintIntentVsOrderAction_Warns()
    {
        var rule = new IntentActionMismatchRule();
        var issues = new List<ReasoningIssue>();
        rule.Apply(new ReasoningResult
        {
            Intent = WellKnown.Intents.Complaint,
            NextAction = "OrderAgent'e yönlendir"
        }, new VerifiedEntities(), issues);

        issues.Should().ContainSingle().Which.Code.Should().Be("intent_action_mismatch");
    }

    [Fact]
    public void IntentActionMismatch_Aligned_NoIssue()
    {
        var rule = new IntentActionMismatchRule();
        var issues = new List<ReasoningIssue>();
        rule.Apply(new ReasoningResult
        {
            Intent = WellKnown.Intents.Complaint,
            NextAction = "ComplaintAgent'e yönlendir"
        }, new VerifiedEntities(), issues);

        issues.Should().BeEmpty();
    }

    // ─── LowConfidenceNoMissingRule ───
    [Fact]
    public void LowConfidenceNoMissing_TriggersInfo()
    {
        var rule = new LowConfidenceNoMissingRule();
        var issues = new List<ReasoningIssue>();
        rule.Apply(new ReasoningResult
        {
            ConfidenceScore = 0.3,
            RequiredInfo = new()
        }, new VerifiedEntities(), issues);

        issues.Should().ContainSingle().Which.Severity.Should().Be(IssueSeverity.Info);
    }

    [Fact]
    public void LowConfidenceNoMissing_HasRequiredInfo_NoIssue()
    {
        var rule = new LowConfidenceNoMissingRule();
        var issues = new List<ReasoningIssue>();
        rule.Apply(new ReasoningResult
        {
            ConfidenceScore = 0.3,
            RequiredInfo = new() { "x" }
        }, new VerifiedEntities(), issues);
        issues.Should().BeEmpty();
    }

    // ─── AssumptionHeavyStepsRule ───
    [Fact]
    public void AssumptionHeavySteps_AssumptionGrounding_Info()
    {
        var rule = new AssumptionHeavyStepsRule();
        var issues = new List<ReasoningIssue>();
        rule.Apply(new ReasoningResult
        {
            Steps = new() { new ReasoningStep { Order = 1, Grounding = "assumption" } }
        }, new VerifiedEntities(), issues);

        issues.Should().ContainSingle().Which.Severity.Should().Be(IssueSeverity.Info);
    }

    // ─── OverconfidentAssumptionsRule ───
    [Fact]
    public void OverconfidentAssumptions_HighConfMany_Warns()
    {
        var rule = new OverconfidentAssumptionsRule();
        var issues = new List<ReasoningIssue>();
        rule.Apply(new ReasoningResult
        {
            ConfidenceScore = 0.9,
            Assumptions = new() { "a", "b", "c", "d" }
        }, new VerifiedEntities(), issues);

        issues.Should().ContainSingle();
    }

    [Fact]
    public void OverconfidentAssumptions_FewAssumptions_NoIssue()
    {
        var rule = new OverconfidentAssumptionsRule();
        var issues = new List<ReasoningIssue>();
        rule.Apply(new ReasoningResult
        {
            ConfidenceScore = 0.9,
            Assumptions = new() { "a" }
        }, new VerifiedEntities(), issues);
        issues.Should().BeEmpty();
    }

    // ─── NotFoundIgnoredRule ───
    [Fact]
    public void NotFoundIgnored_NoVerificationIntent_Errors()
    {
        var rule = new NotFoundIgnoredRule();
        var issues = new List<ReasoningIssue>();
        var verified = new VerifiedEntities
        {
            OrderId = new VerifiedEntity
            {
                Value = "9999",
                Verification = EntityVerification.NotFoundInDb
            }
        };
        // NotFoundIgnoredRule dil-bağımsız hâle getirildi: artık NextAction metnine değil
        // confidence + requiredInfo + assumptions üçlüsüne bakıyor. ConfidenceScore
        // default'u 0.5 (< 0.55 eşiği) olduğu için kural "model zaten belirsiz" diyerek
        // sessiz kalıyordu — overconfidence senaryosunu açıkça kurmak gerekiyor.
        rule.Apply(new ReasoningResult
        {
            NextAction = "OrderAgent'e yönlendir",
            ConfidenceScore = 0.85
        }, verified, issues);

        issues.Should().ContainSingle().Which.Severity.Should().Be(IssueSeverity.Error);
    }

    [Fact]
    public void NotFoundIgnored_LowConfidence_NoIssue()
    {
        var rule = new NotFoundIgnoredRule();
        var issues = new List<ReasoningIssue>();
        var verified = new VerifiedEntities
        {
            OrderId = new VerifiedEntity
            {
                Value = "9999",
                Verification = EntityVerification.NotFoundInDb
            }
        };

        // Düşük confidence → model belirsizliğinin farkında, uyarı üretilmez.
        rule.Apply(new ReasoningResult
        {
            NextAction = "OrderAgent'e yönlendir",
            ConfidenceScore = 0.4
        }, verified, issues);

        issues.Should().BeEmpty();
    }

    [Fact]
    public void NotFoundIgnored_HasVerificationIntent_NoIssue()
    {
        var rule = new NotFoundIgnoredRule();
        var issues = new List<ReasoningIssue>();
        var verified = new VerifiedEntities
        {
            OrderId = new VerifiedEntity
            {
                Value = "9999",
                Verification = EntityVerification.NotFoundInDb
            }
        };
        // Yüksek confidence olmasına rağmen requiredInfo entity'yi açıkça sorguladığı için
        // uyarı üretilmez. (Eskiden ConfidenceScore ayarlanmadığından bu test default 0.5
        // sayesinde, yani yanlış sebeple geçiyordu.)
        rule.Apply(new ReasoningResult
        {
            NextAction = "Kullanıcıya sipariş numarasını doğrulat",
            ConfidenceScore = 0.85,
            RequiredInfo = new List<string> { "order_id=9999 doğrulanmalı" }
        }, verified, issues);
        issues.Should().BeEmpty();
    }

    // ─── SubTasksIgnoredRule ───
    [Fact]
    public void SubTasksIgnored_NextActionMissesAgents_Warns()
    {
        var rule = new SubTasksIgnoredRule();
        var issues = new List<ReasoningIssue>();
        rule.Apply(new ReasoningResult
        {
            SubTasks = new()
            {
                new SubTask { Order = 1, TargetAgent = "OrderAgent" },
                new SubTask { Order = 2, TargetAgent = "ComplaintAgent" }
            },
            NextAction = "orderagent'e yönlendir"
        }, new VerifiedEntities(), issues);

        issues.Should().ContainSingle().Which.Code.Should().Be("subtasks_ignored");
    }

    [Fact]
    public void SubTasksIgnored_OneSubTask_NoIssue()
    {
        var rule = new SubTasksIgnoredRule();
        var issues = new List<ReasoningIssue>();
        rule.Apply(new ReasoningResult
        {
            SubTasks = new() { new SubTask { Order = 1, TargetAgent = "OrderAgent" } }
        }, new VerifiedEntities(), issues);
        issues.Should().BeEmpty();
    }

    // ─── Top-level checker ───
    [Fact]
    public void Check_NoIssues_EmptyList()
    {
        var issues = _checker.Check(new ReasoningResult { ConfidenceScore = 0.7 }, new VerifiedEntities());
        issues.Should().BeEmpty();
    }

    [Fact]
    public void Check_AggregatesAllRules()
    {
        // Trigger multiple rules at once
        var issues = _checker.Check(new ReasoningResult
        {
            ConfidenceScore = 0.9,
            NextAction = "Kullanıcıdan iste",
            Assumptions = new() { "a", "b", "c", "d" }
        }, new VerifiedEntities());

        issues.Count.Should().BeGreaterThanOrEqualTo(2);
    }
}

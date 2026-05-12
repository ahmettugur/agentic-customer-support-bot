// Tests/Services/ReasoningSanityCheckerTests.cs

using CustomerSupportBot.Api.Models;
using CustomerSupportBot.Api.Services;
using CustomerSupportBot.Api.Models;
using CustomerSupportBot.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Tests.Services;

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
                Value = "ORD-1",
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
    public void IntentActionMismatch_ComplaintIntentVsOrderInquiryAction_Warns()
    {
        var rule = new IntentActionMismatchRule();
        var issues = new List<ReasoningIssue>();
        rule.Apply(new ReasoningResult
        {
            Intent = WellKnown.Intents.Complaint,
            NextAction = "OrderInquiryAgent'e yönlendir"
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
                Value = "ORD-9999",
                Verification = EntityVerification.NotFoundInDb
            }
        };
        rule.Apply(new ReasoningResult
        {
            NextAction = "OrderInquiryAgent'e yönlendir"
        }, verified, issues);

        issues.Should().ContainSingle().Which.Severity.Should().Be(IssueSeverity.Error);
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
                Value = "ORD-9999",
                Verification = EntityVerification.NotFoundInDb
            }
        };
        rule.Apply(new ReasoningResult
        {
            NextAction = "Kullanıcıya sipariş numarasını doğrulat"
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
                new SubTask { Order = 1, TargetAgent = "OrderInquiryAgent" },
                new SubTask { Order = 2, TargetAgent = "ComplaintAgent" }
            },
            NextAction = "orderinquiryagent'e yönlendir"
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
            SubTasks = new() { new SubTask { Order = 1, TargetAgent = "OrderInquiryAgent" } }
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

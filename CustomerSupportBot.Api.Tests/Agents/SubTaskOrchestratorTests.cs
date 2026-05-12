using CustomerSupportBot.Api.Agents;
using CustomerSupportBot.Api.Agents;
using CustomerSupportBot.Api.Models;
using CustomerSupportBot.Api.Models;

namespace CustomerSupportBot.Api.Tests.Agents;

public class SubTaskOrchestratorTests
{
    [Fact]
    public void IsCompoundQuery_Null_False() =>
        SubTaskOrchestrator.IsCompoundQuery(null).Should().BeFalse();

    [Fact]
    public void IsCompoundQuery_LessThanTwoSubTasks_False()
    {
        var r = new ReasoningResult
        {
            SubTasks = new() { new SubTask { TargetAgent = "A" } }
        };
        SubTaskOrchestrator.IsCompoundQuery(r).Should().BeFalse();
    }

    [Fact]
    public void IsCompoundQuery_TwoSubTasksSameAgent_False()
    {
        var r = new ReasoningResult
        {
            SubTasks = new()
            {
                new SubTask { TargetAgent = "OrderInquiryAgent" },
                new SubTask { TargetAgent = "OrderInquiryAgent" }
            }
        };
        SubTaskOrchestrator.IsCompoundQuery(r).Should().BeFalse();
    }

    [Fact]
    public void IsCompoundQuery_TwoDistinctAgents_True()
    {
        var r = new ReasoningResult
        {
            SubTasks = new()
            {
                new SubTask { TargetAgent = "OrderInquiryAgent" },
                new SubTask { TargetAgent = "ComplaintAgent" }
            }
        };
        SubTaskOrchestrator.IsCompoundQuery(r).Should().BeTrue();
    }

    [Fact]
    public void IsCompoundQuery_BlankAgentsSkipped_False()
    {
        var r = new ReasoningResult
        {
            SubTasks = new()
            {
                new SubTask { TargetAgent = "" },
                new SubTask { TargetAgent = "  " }
            }
        };
        SubTaskOrchestrator.IsCompoundQuery(r).Should().BeFalse();
    }

    [Fact]
    public void CreateSubTaskReasoning_PreservesParentIntentWhenSubBlank()
    {
        var parent = new ReasoningResult
        {
            Intent = "parent_intent",
            Rationale = "parent_rationale",
            SubTasks = new() { new SubTask { Order = 1 }, new SubTask { Order = 2 } }
        };
        var sub = new SubTask { Order = 1, Description = "alt iş", TargetAgent = "AgentX" };
        var derived = SubTaskOrchestrator.CreateSubTaskReasoning(parent, sub);
        derived.Intent.Should().Be("parent_intent");
        derived.Rationale.Should().Be("parent_rationale");
        derived.NextAction.Should().Contain("AgentX");
        derived.NextAction.Should().Contain("alt iş");
        derived.SubTasks.Should().BeEmpty(); // recursion guard
        derived.Confidence.Should().Be(WellKnown.Confidence.High);
        derived.ConfidenceScore.Should().Be(0.85);
    }

    [Fact]
    public void CreateSubTaskReasoning_UsesSubIntentWhenProvided()
    {
        var parent = new ReasoningResult { Intent = "parent" };
        var sub = new SubTask { Order = 1, Intent = "child", Description = "x" };
        var derived = SubTaskOrchestrator.CreateSubTaskReasoning(parent, sub);
        derived.Intent.Should().Be("child");
    }

    [Fact]
    public void CreateSubTaskReasoning_BlankTargetAgent_UsesDescriptionAsAction()
    {
        var parent = new ReasoningResult();
        var sub = new SubTask { Description = "salt iş", TargetAgent = "" };
        SubTaskOrchestrator.CreateSubTaskReasoning(parent, sub).NextAction.Should().Be("salt iş");
    }

    [Fact]
    public void FormatSubTaskQuery_NoEntities_DescriptionOnly()
    {
        var sub = new SubTask { Description = "şikayet ver" };
        SubTaskOrchestrator.FormatSubTaskQuery(sub).Should().Be("şikayet ver");
    }

    [Fact]
    public void FormatSubTaskQuery_WithEntities_AppendsParens()
    {
        var sub = new SubTask
        {
            Description = "sipariş sor",
            Entities = new() { ["order_id"] = "ORD-1" }
        };
        var q = SubTaskOrchestrator.FormatSubTaskQuery(sub);
        q.Should().Contain("sipariş sor");
        q.Should().Contain("order_id=ORD-1");
    }

    [Fact]
    public void FormatSubTaskQuery_BlankDescription_FallsBackToIntent()
    {
        var sub = new SubTask { Description = "", Intent = "fallback_intent" };
        SubTaskOrchestrator.FormatSubTaskQuery(sub).Should().Be("fallback_intent");
    }

    [Fact]
    public void FormatSubTaskResult_FormatsHeader()
    {
        var sub = new SubTask { Order = 2, Description = "tema" };
        var formatted = SubTaskOrchestrator.FormatSubTaskResult(sub, "  yanıt  ");
        formatted.Should().StartWith("**2) tema**");
        formatted.Should().Contain("yanıt");
    }

    [Fact]
    public void AggregateSubTaskResults_JoinsWithSeparator()
    {
        var parts = new[] { "a", "b", "c" };
        SubTaskOrchestrator.AggregateSubTaskResults(parts)
            .Should().Be("a\n\n---\n\nb\n\n---\n\nc");
    }
}

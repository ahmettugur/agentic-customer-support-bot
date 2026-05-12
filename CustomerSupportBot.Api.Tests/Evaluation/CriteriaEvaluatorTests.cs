// Tests/Evaluation/CriteriaEvaluatorTests.cs
using CustomerSupportBot.Api.Evaluation;
using FluentAssertions;

namespace CustomerSupportBot.Tests.Evaluation;

public class CriteriaEvaluatorTests
{
    [Fact]
    public void ResponseContains_Matches_Pass()
    {
        var ctx = new ScenarioRunContext { Response = "Sipariş ORD-1 teslim edildi." };
        var r = CriteriaEvaluator.Evaluate("response contains 'teslim edildi'", ctx);
        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void ResponseContains_NoMatch_Fail()
    {
        var ctx = new ScenarioRunContext { Response = "Bilgi yok." };
        var r = CriteriaEvaluator.Evaluate("response contains 'teslim edildi'", ctx);
        r.Passed.Should().BeFalse();
    }

    [Fact]
    public void ResponseContains_OrAlternative_FirstMatchPass()
    {
        var ctx = new ScenarioRunContext { Response = "kargoya verildi" };
        var r = CriteriaEvaluator.Evaluate("response contains 'teslim' OR 'kargo'", ctx);
        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void TurnCount_LessOrEqual_Pass()
    {
        var ctx = new ScenarioRunContext { IterationCount = 3 };
        var r = CriteriaEvaluator.Evaluate("turn_count <= 5", ctx);
        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void TurnCount_Greater_Fail()
    {
        var ctx = new ScenarioRunContext { IterationCount = 10 };
        var r = CriteriaEvaluator.Evaluate("turn_count <= 5", ctx);
        r.Passed.Should().BeFalse();
    }

    [Fact]
    public void ToolCalled_True_Pass()
    {
        var ctx = new ScenarioRunContext
        {
            ToolsCalled = new List<string> { "order_status_tool" }
        };
        var r = CriteriaEvaluator.Evaluate("order_status_tool called", ctx);
        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void ToolCalled_False_Fail()
    {
        var ctx = new ScenarioRunContext { ToolsCalled = new List<string>() };
        var r = CriteriaEvaluator.Evaluate("order_status_tool called", ctx);
        r.Passed.Should().BeFalse();
    }

    [Fact]
    public void ToolNotCalled_NotInList_Pass()
    {
        var ctx = new ScenarioRunContext { ToolsCalled = new List<string> { "other_tool" } };
        var r = CriteriaEvaluator.Evaluate("complaint_tool NOT called", ctx);
        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void NoExtraToolCalls_WithinExpected_Pass()
    {
        var ctx = new ScenarioRunContext
        {
            ExpectedTools = new List<string> { "a", "b" },
            ToolsCalled = new List<string> { "a", "b" }
        };
        var r = CriteriaEvaluator.Evaluate("no extra tool calls", ctx);
        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void NoExtraToolCalls_TooMany_Fail()
    {
        var ctx = new ScenarioRunContext
        {
            ExpectedTools = new List<string> { "a" },
            ToolsCalled = new List<string> { "a", "b", "c" }
        };
        var r = CriteriaEvaluator.Evaluate("no extra tool calls", ctx);
        r.Passed.Should().BeFalse();
    }

    [Fact]
    public void OrderIdInResponse_Match_Pass()
    {
        var ctx = new ScenarioRunContext { Response = "Sipariş ORD-42 oluşturuldu." };
        var r = CriteriaEvaluator.Evaluate("order id returned", ctx);
        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void ComplaintIdInResponse_Match_Pass()
    {
        var ctx = new ScenarioRunContext { Response = "Şikayet CMP-7 alındı." };
        var r = CriteriaEvaluator.Evaluate("complaint id returned", ctx);
        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void UnknownCriterion_MarkedManualReview()
    {
        var ctx = new ScenarioRunContext();
        var r = CriteriaEvaluator.Evaluate("response sentiment is happy", ctx);
        r.Passed.Should().BeFalse();
        r.Skipped.Should().Be("manual_review_needed");
    }
}

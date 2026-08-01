// Tests/Evaluation/CriteriaEvaluatorTests.cs
using CustomerSupportBot.Adapters.Agents.Evaluation;
using CustomerSupportBot.Application.Ports.Inbound;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Api.Tests.Evaluation;

public class CriteriaEvaluatorTests
{
    /// <summary>
    /// EvaluationRunner'ın gerçek koşumda kurduğu EvalItem'ı taklit eder — ctx.ToolsCalled'ı
    /// sentetik FunctionCallContent'lere çevirir ki tool_called/tool_not_called (EvalChecks
    /// tabanlı) testleri gerçek mekanizmayı egzersiz etsin.
    /// </summary>
    private static EvalItem BuildItem(
        ScenarioRunContext ctx,
        string query = "q",
        List<ExpectedToolCall>? expectedToolCalls = null,
        List<FunctionCallContent>? toolCallsWithArgs = null)
    {
        var conversation = new List<ChatMessage> { new(ChatRole.User, query) };
        if (toolCallsWithArgs != null)
        {
            conversation.AddRange(toolCallsWithArgs.Select(fc =>
                new ChatMessage(ChatRole.Assistant, [fc])));
        }
        else
        {
            conversation.AddRange(ctx.ToolsCalled.Select(toolName =>
                new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(Guid.NewGuid().ToString(), toolName)])));
        }
        return new EvalItem(query, ctx.Response ?? "", conversation) { ExpectedToolCalls = expectedToolCalls };
    }

    [Fact]
    public void ContainsAny_Matches_Pass()
    {
        var ctx = new ScenarioRunContext { Response = "Sipariş 1030 teslim edildi." };
        var spec = new CriterionSpec { Type = "contains_any", Values = ["teslim edildi"] };
        var r = CriteriaEvaluator.Evaluate(spec, BuildItem(ctx), ctx);
        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void ContainsAny_NoMatch_Fail()
    {
        var ctx = new ScenarioRunContext { Response = "Bilgi yok." };
        var spec = new CriterionSpec { Type = "contains_any", Values = ["teslim edildi"] };
        var r = CriteriaEvaluator.Evaluate(spec, BuildItem(ctx), ctx);
        r.Passed.Should().BeFalse();
    }

    [Fact]
    public void ContainsAny_OrAlternative_FirstMatchPass()
    {
        var ctx = new ScenarioRunContext { Response = "kargoya verildi" };
        var spec = new CriterionSpec { Type = "contains_any", Values = ["teslim", "kargo"] };
        var r = CriteriaEvaluator.Evaluate(spec, BuildItem(ctx), ctx);
        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void TurnCount_LessOrEqual_Pass()
    {
        var ctx = new ScenarioRunContext { IterationCount = 3 };
        var spec = new CriterionSpec { Type = "turn_count", Op = "<=", Value = 5 };
        var r = CriteriaEvaluator.Evaluate(spec, BuildItem(ctx), ctx);
        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void TurnCount_Greater_Fail()
    {
        var ctx = new ScenarioRunContext { IterationCount = 10 };
        var spec = new CriterionSpec { Type = "turn_count", Op = "<=", Value = 5 };
        var r = CriteriaEvaluator.Evaluate(spec, BuildItem(ctx), ctx);
        r.Passed.Should().BeFalse();
    }

    [Fact]
    public void ToolCalled_True_Pass()
    {
        var ctx = new ScenarioRunContext { ToolsCalled = new List<string> { "order_status_tool" } };
        var spec = new CriterionSpec { Type = "tool_called", Values = ["order_status_tool"] };
        var r = CriteriaEvaluator.Evaluate(spec, BuildItem(ctx), ctx);
        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void ToolCalled_False_Fail()
    {
        var ctx = new ScenarioRunContext { ToolsCalled = new List<string>() };
        var spec = new CriterionSpec { Type = "tool_called", Values = ["order_status_tool"] };
        var r = CriteriaEvaluator.Evaluate(spec, BuildItem(ctx), ctx);
        r.Passed.Should().BeFalse();
    }

    [Fact]
    public void ToolNotCalled_NotInList_Pass()
    {
        var ctx = new ScenarioRunContext { ToolsCalled = new List<string> { "other_tool" } };
        var spec = new CriterionSpec { Type = "tool_not_called", Values = ["complaint_tool"] };
        var r = CriteriaEvaluator.Evaluate(spec, BuildItem(ctx), ctx);
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
        var spec = new CriterionSpec { Type = "no_extra_tool_calls" };
        var r = CriteriaEvaluator.Evaluate(spec, BuildItem(ctx), ctx);
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
        var spec = new CriterionSpec { Type = "no_extra_tool_calls" };
        var r = CriteriaEvaluator.Evaluate(spec, BuildItem(ctx), ctx);
        r.Passed.Should().BeFalse();
    }

    [Fact]
    public void OrderIdInResponse_Match_Pass()
    {
        var ctx = new ScenarioRunContext { Response = "Sipariş 1042 oluşturuldu." };
        var spec = new CriterionSpec { Type = "order_id_returned" };
        var r = CriteriaEvaluator.Evaluate(spec, BuildItem(ctx), ctx);
        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void ComplaintIdInResponse_Match_Pass()
    {
        var ctx = new ScenarioRunContext { Response = "Şikayet 1001 alındı." };
        var spec = new CriterionSpec { Type = "complaint_id_returned" };
        var r = CriteriaEvaluator.Evaluate(spec, BuildItem(ctx), ctx);
        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void UnknownCriterion_MarkedManualReview()
    {
        var ctx = new ScenarioRunContext();
        var spec = new CriterionSpec { Type = "response_sentiment_is_happy" };
        var r = CriteriaEvaluator.Evaluate(spec, BuildItem(ctx), ctx);
        r.Passed.Should().BeFalse();
        r.Skipped.Should().Be("manual_review_needed");
    }

    [Fact]
    public void ManualReview_MarkedManualReview()
    {
        var ctx = new ScenarioRunContext();
        var spec = new CriterionSpec { Type = "manual_review", Note = "no_hallucinated_price" };
        var r = CriteriaEvaluator.Evaluate(spec, BuildItem(ctx), ctx);
        r.Passed.Should().BeFalse();
        r.Skipped.Should().Be("manual_review_needed");
        r.Evaluation.Should().Be("no_hallucinated_price");
    }

    // ─── tool_call_args_match (EvalChecks.ToolCallArgsMatch — gerçek built-in) ───

    [Fact]
    public void ToolCallArgsMatch_NameAndArgsMatch_Pass()
    {
        var ctx = new ScenarioRunContext();
        var expected = new List<ExpectedToolCall>
        {
            new("order_status_tool", new Dictionary<string, object> { ["orderId"] = "1042" })
        };
        var actualCall = new FunctionCallContent("call-1", "order_status_tool",
            new Dictionary<string, object?> { ["orderId"] = "1042" });
        var item = BuildItem(ctx, expectedToolCalls: expected, toolCallsWithArgs: [actualCall]);

        var spec = new CriterionSpec { Type = "tool_call_args_match" };
        var r = CriteriaEvaluator.Evaluate(spec, item, ctx);
        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void ToolCallArgsMatch_ArgMismatch_Fail()
    {
        var ctx = new ScenarioRunContext();
        var expected = new List<ExpectedToolCall>
        {
            new("order_status_tool", new Dictionary<string, object> { ["orderId"] = "1042" })
        };
        var actualCall = new FunctionCallContent("call-1", "order_status_tool",
            new Dictionary<string, object?> { ["orderId"] = "9999" });
        var item = BuildItem(ctx, expectedToolCalls: expected, toolCallsWithArgs: [actualCall]);

        var spec = new CriterionSpec { Type = "tool_call_args_match" };
        var r = CriteriaEvaluator.Evaluate(spec, item, ctx);
        r.Passed.Should().BeFalse();
    }

    [Fact]
    public void ToolCallArgsMatch_NoExpectations_Pass()
    {
        var ctx = new ScenarioRunContext();
        var item = BuildItem(ctx);
        var spec = new CriterionSpec { Type = "tool_call_args_match" };
        var r = CriteriaEvaluator.Evaluate(spec, item, ctx);
        r.Passed.Should().BeTrue();
    }
}

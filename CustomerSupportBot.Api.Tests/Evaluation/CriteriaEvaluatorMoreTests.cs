// Tests/Evaluation/CriteriaEvaluatorMoreTests.cs
// CriteriaEvaluator için ek branch coverage.
using CustomerSupportBot.Adapters.Agents.Evaluation;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Domain.Model;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Api.Tests.Evaluation;

public class CriteriaEvaluatorMoreTests
{
    private static EvalItem BuildItem(ScenarioRunContext ctx, string query = "q")
    {
        var conversation = new List<ChatMessage> { new(ChatRole.User, query) };
        conversation.AddRange(ctx.ToolsCalled.Select(toolName =>
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(Guid.NewGuid().ToString(), toolName)])));
        return new EvalItem(query, ctx.Response ?? "", conversation);
    }

    // ─── turn_count operatör varyantları ───
    [Theory]
    [InlineData("==", 3, 3, true)]
    [InlineData("==", 3, 4, false)]
    [InlineData("=", 3, 3, true)]
    [InlineData("<", 5, 4, true)]
    [InlineData("<", 5, 5, false)]
    [InlineData(">=", 2, 2, true)]
    [InlineData(">=", 2, 1, false)]
    [InlineData(">", 1, 2, true)]
    [InlineData(">", 1, 1, false)]
    public void TurnCount_AllOperators_BehaveCorrectly(string op, int value, int actual, bool expected)
    {
        var ctx = new ScenarioRunContext { IterationCount = actual };
        var spec = new CriterionSpec { Type = "turn_count", Op = op, Value = value };
        var r = CriteriaEvaluator.Evaluate(spec, BuildItem(ctx), ctx);
        r.Passed.Should().Be(expected);
    }

    // ─── no missing_param_tool ───
    [Fact]
    public void NoMissingParam_NoValidationError_Pass()
    {
        var ctx = new ScenarioRunContext
        {
            SpecialistReasonings = new List<SpecialistReasoning>
            {
                new()
                {
                    AgentName = "OrderAgent",
                    ResultConfidence = 0.9,
                    PreToolCheck = new() { CanProceed = true }
                }
            }
        };
        var spec = new CriterionSpec { Type = "no_missing_param_tool" };
        var r = CriteriaEvaluator.Evaluate(spec, BuildItem(ctx), ctx);
        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void NoMissingParam_HasValidationError_Fail()
    {
        var ctx = new ScenarioRunContext
        {
            SpecialistReasonings = new List<SpecialistReasoning>
            {
                new()
                {
                    AgentName = "OrderAgent",
                    ResultConfidence = 0.0,
                    PreToolCheck = new() { CanProceed = true }
                }
            }
        };
        var spec = new CriterionSpec { Type = "no_missing_param_tool" };
        var r = CriteriaEvaluator.Evaluate(spec, BuildItem(ctx), ctx);
        r.Passed.Should().BeFalse();
    }

    // ─── agent requests field ───
    [Fact]
    public void AgentRequests_ResponseAsksCustomerId_Pass()
    {
        var ctx = new ScenarioRunContext
        {
            Response = "Lütfen müşteri kimlik numaranızı paylaşır mısınız?"
        };
        var spec = new CriterionSpec { Type = "agent_requests_field", Field = "customer_id" };
        var r = CriteriaEvaluator.Evaluate(spec, BuildItem(ctx), ctx);
        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void AgentRequests_ResponseAsksOrderNumber_Pass()
    {
        var ctx = new ScenarioRunContext { Response = "Sipariş numaranız nedir?" };
        var spec = new CriterionSpec { Type = "agent_requests_field", Field = "order_id" };
        var r = CriteriaEvaluator.Evaluate(spec, BuildItem(ctx), ctx);
        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void AgentRequests_AwaitingUserInputTermination_Pass()
    {
        var ctx = new ScenarioRunContext
        {
            Response = "Devam edebilmem için bilgiye ihtiyacım var.",
            TerminationReason = "awaiting_user_input"
        };
        var spec = new CriterionSpec { Type = "agent_requests_field", Field = "customer_id" };
        var r = CriteriaEvaluator.Evaluate(spec, BuildItem(ctx), ctx);
        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void AgentRequests_NoQuestion_Fail()
    {
        var ctx = new ScenarioRunContext { Response = "İşlem başarıyla tamamlandı." };
        var spec = new CriterionSpec { Type = "agent_requests_field", Field = "customer_id" };
        var r = CriteriaEvaluator.Evaluate(spec, BuildItem(ctx), ctx);
        r.Passed.Should().BeFalse();
    }

    // ─── customer_id_used ───
    [Fact]
    public void CustomerIdUsed_CollectedInPreToolCheck_Pass()
    {
        var ctx = new ScenarioRunContext
        {
            SpecialistReasonings = new List<SpecialistReasoning>
            {
                new()
                {
                    AgentName = "OrderAgent",
                    PreToolCheck = new()
                    {
                        CanProceed = true,
                        CollectedParams = new List<string> { "customer_id=1027" }
                    }
                }
            }
        };
        var spec = new CriterionSpec { Type = "customer_id_used" };
        var r = CriteriaEvaluator.Evaluate(spec, BuildItem(ctx), ctx);
        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void CustomerIdUsed_NotCollected_Fail()
    {
        var ctx = new ScenarioRunContext
        {
            SpecialistReasonings = new List<SpecialistReasoning>
            {
                new()
                {
                    AgentName = "OrderAgent",
                    PreToolCheck = new() { CanProceed = true, CollectedParams = new() }
                }
            }
        };
        var spec = new CriterionSpec { Type = "customer_id_used" };
        var r = CriteriaEvaluator.Evaluate(spec, BuildItem(ctx), ctx);
        r.Passed.Should().BeFalse();
    }

    // ─── order_status_contains ───
    [Fact]
    public void OrderStatus_ResponseHasStatusKeyword_Pass()
    {
        var ctx = new ScenarioRunContext { Response = "Siparişiniz kargo aşamasında." };
        var spec = new CriterionSpec { Type = "order_status_contains" };
        var r = CriteriaEvaluator.Evaluate(spec, BuildItem(ctx), ctx);
        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void OrderStatus_NoKeyword_Fail()
    {
        var ctx = new ScenarioRunContext { Response = "Yardımcı olamadım." };
        var spec = new CriterionSpec { Type = "order_status_contains" };
        var r = CriteriaEvaluator.Evaluate(spec, BuildItem(ctx), ctx);
        r.Passed.Should().BeFalse();
    }

    // ─── ID returned (negative paths) ───
    [Fact]
    public void OrderIdReturned_MissingId_Fail()
    {
        var ctx = new ScenarioRunContext { Response = "İşlem yapıldı." };
        var spec = new CriterionSpec { Type = "order_id_returned" };
        var r = CriteriaEvaluator.Evaluate(spec, BuildItem(ctx), ctx);
        r.Passed.Should().BeFalse();
    }

    [Fact]
    public void ComplaintIdReturned_MissingId_Fail()
    {
        var ctx = new ScenarioRunContext { Response = "Şikayetiniz alındı." };
        var spec = new CriterionSpec { Type = "complaint_id_returned" };
        var r = CriteriaEvaluator.Evaluate(spec, BuildItem(ctx), ctx);
        r.Passed.Should().BeFalse();
    }

    // ─── ContainsAny: response null ───
    [Fact]
    public void ContainsAny_NullResponse_Fail()
    {
        var ctx = new ScenarioRunContext { Response = null };
        var spec = new CriterionSpec { Type = "contains_any", Values = ["merhaba"] };
        var r = CriteriaEvaluator.Evaluate(spec, BuildItem(ctx), ctx);
        r.Passed.Should().BeFalse();
    }

    // ─── Tool NOT called: actually called — fail ───
    [Fact]
    public void ToolNotCalled_Called_Fail()
    {
        var ctx = new ScenarioRunContext
        {
            ToolsCalled = new List<string> { "complaint_tool" }
        };
        var spec = new CriterionSpec { Type = "tool_not_called", Values = ["complaint_tool"] };
        var r = CriteriaEvaluator.Evaluate(spec, BuildItem(ctx), ctx);
        r.Passed.Should().BeFalse();
    }
}

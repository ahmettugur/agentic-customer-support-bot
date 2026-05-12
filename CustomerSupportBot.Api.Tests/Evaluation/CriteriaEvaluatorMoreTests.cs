// Tests/Evaluation/CriteriaEvaluatorMoreTests.cs
// CriteriaEvaluator için ek branch coverage.
using CustomerSupportBot.Api.Evaluation;
using CustomerSupportBot.Api.Models;

namespace CustomerSupportBot.Api.Tests.Evaluation;

public class CriteriaEvaluatorMoreTests
{
    // ─── turn_count operatör varyantları ───
    [Theory]
    [InlineData("turn_count == 3", 3, true)]
    [InlineData("turn_count == 3", 4, false)]
    [InlineData("turn_count = 3", 3, true)]
    [InlineData("turn_count < 5", 4, true)]
    [InlineData("turn_count < 5", 5, false)]
    [InlineData("turn_count >= 2", 2, true)]
    [InlineData("turn_count >= 2", 1, false)]
    [InlineData("turn_count > 1", 2, true)]
    [InlineData("turn_count > 1", 1, false)]
    public void TurnCount_AllOperators_BehaveCorrectly(string criterion, int actual, bool expected)
    {
        var ctx = new ScenarioRunContext { IterationCount = actual };
        var r = CriteriaEvaluator.Evaluate(criterion, ctx);
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
                    AgentName = "OrderInquiryAgent",
                    ResultConfidence = 0.9,
                    PreToolCheck = new() { CanProceed = true }
                }
            }
        };
        var r = CriteriaEvaluator.Evaluate("no missing_param_tool error", ctx);
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
                    AgentName = "OrderInquiryAgent",
                    ResultConfidence = 0.0,
                    PreToolCheck = new() { CanProceed = true }
                }
            }
        };
        var r = CriteriaEvaluator.Evaluate("no missing_param_tool error", ctx);
        r.Passed.Should().BeFalse();
    }

    // ─── agent requests ... ───
    [Fact]
    public void AgentRequests_ResponseAsksCustomerId_Pass()
    {
        var ctx = new ScenarioRunContext
        {
            Response = "Lütfen müşteri kimlik numaranızı paylaşır mısınız?"
        };
        var r = CriteriaEvaluator.Evaluate("agent requests customer_id", ctx);
        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void AgentRequests_ResponseAsksOrderNumber_Pass()
    {
        var ctx = new ScenarioRunContext { Response = "Sipariş numaranız nedir?" };
        var r = CriteriaEvaluator.Evaluate("agent requests order_id", ctx);
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
        var r = CriteriaEvaluator.Evaluate("requests customer_id", ctx);
        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void AgentRequests_NoQuestion_Fail()
    {
        var ctx = new ScenarioRunContext { Response = "İşlem başarıyla tamamlandı." };
        var r = CriteriaEvaluator.Evaluate("agent requests customer_id", ctx);
        r.Passed.Should().BeFalse();
    }

    // ─── customer_id used / uses customer_id ───
    [Fact]
    public void CustomerIdUsed_CollectedInPreToolCheck_Pass()
    {
        var ctx = new ScenarioRunContext
        {
            SpecialistReasonings = new List<SpecialistReasoning>
            {
                new()
                {
                    AgentName = "OrderInquiryAgent",
                    PreToolCheck = new()
                    {
                        CanProceed = true,
                        CollectedParams = new List<string> { "customer_id=CUST-1990" }
                    }
                }
            }
        };
        var r = CriteriaEvaluator.Evaluate("customer_id used correctly", ctx);
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
                    AgentName = "OrderInquiryAgent",
                    PreToolCheck = new() { CanProceed = true, CollectedParams = new() }
                }
            }
        };
        var r = CriteriaEvaluator.Evaluate("uses customer_id", ctx);
        r.Passed.Should().BeFalse();
    }

    // ─── no_hallucinated / no hallucination ───
    // Not: FinalCritique/ResponseCritique tipi kaldırıldı; bu kriter artık
    // veri olmadan değerlendirilmiyor. İlgili testler düşürüldü.

    // ─── order status ───
    [Fact]
    public void OrderStatus_ResponseHasStatusKeyword_Pass()
    {
        var ctx = new ScenarioRunContext { Response = "Siparişiniz kargo aşamasında." };
        var r = CriteriaEvaluator.Evaluate("response includes order status", ctx);
        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void OrderStatus_NoKeyword_Fail()
    {
        var ctx = new ScenarioRunContext { Response = "Yardımcı olamadım." };
        var r = CriteriaEvaluator.Evaluate("sipariş durumu var mı", ctx);
        r.Passed.Should().BeFalse();
    }

    // ─── ID returned (negative paths) ───
    [Fact]
    public void OrderIdReturned_MissingId_Fail()
    {
        var ctx = new ScenarioRunContext { Response = "İşlem yapıldı." };
        var r = CriteriaEvaluator.Evaluate("order id returned", ctx);
        r.Passed.Should().BeFalse();
    }

    [Fact]
    public void ComplaintIdReturned_MissingId_Fail()
    {
        var ctx = new ScenarioRunContext { Response = "Şikayetiniz alındı." };
        var r = CriteriaEvaluator.Evaluate("complaint id returned", ctx);
        r.Passed.Should().BeFalse();
    }

    // ─── ResponseContains: response null ───
    [Fact]
    public void ResponseContains_NullResponse_Fail()
    {
        var ctx = new ScenarioRunContext { Response = null };
        var r = CriteriaEvaluator.Evaluate("response contains 'merhaba'", ctx);
        r.Passed.Should().BeFalse();
    }

    // ─── Tool NOT called: actually called → fail ───
    [Fact]
    public void ToolNotCalled_Called_Fail()
    {
        var ctx = new ScenarioRunContext
        {
            ToolsCalled = new List<string> { "complaint_tool" }
        };
        var r = CriteriaEvaluator.Evaluate("complaint_tool NOT called", ctx);
        r.Passed.Should().BeFalse();
    }
}

// Tests/Services/PlanningResultParserTests.cs

using CustomerSupportBot.Api.Services;

namespace CustomerSupportBot.Api.Tests.Services;

public class PlanningResultParserTests
{
    [Fact]
    public void TryParse_NullInput_ReturnsNull()
    {
        PlanningResultParser.TryParse(null).Should().BeNull();
    }

    [Fact]
    public void TryParse_EmptyInput_ReturnsNull()
    {
        PlanningResultParser.TryParse("").Should().BeNull();
        PlanningResultParser.TryParse("   ").Should().BeNull();
    }

    [Fact]
    public void TryParse_NoJson_ReturnsNull()
    {
        PlanningResultParser.TryParse("just plain text without braces").Should().BeNull();
    }

    [Fact]
    public void TryParse_MalformedJson_ReturnsNull()
    {
        PlanningResultParser.TryParse("{\"selectedAgent\": ").Should().BeNull();
    }

    [Fact]
    public void TryParse_ValidJsonInFence_ParsesCorrectly()
    {
        var input = """
            ```json
            {
                "detectedIntent": "order_inquiry",
                "intentConfidence": 0.85,
                "selectedAgent": "OrderInquiryAgent",
                "rationale": "User asked about order status",
                "needsClarification": false
            }
            ```
            """;

        var plan = PlanningResultParser.TryParse(input);

        plan.Should().NotBeNull();
        plan!.DetectedIntent.Should().Be("order_inquiry");
        plan.IntentConfidence.Should().Be(0.85);
        plan.SelectedAgent.Should().Be("OrderInquiryAgent");
        plan.NeedsClarification.Should().BeFalse();
    }

    [Fact]
    public void TryParse_PlainJson_ParsesCorrectly()
    {
        var input = """
            {"selectedAgent":"ProductInquiryAgent","intentConfidence":0.9,"detectedIntent":"product_info"}
            """;

        var plan = PlanningResultParser.TryParse(input);

        plan.Should().NotBeNull();
        plan!.SelectedAgent.Should().Be("ProductInquiryAgent");
        plan.IntentConfidence.Should().Be(0.9);
    }

    [Fact]
    public void TryParse_StringConfidence_ConvertedToScore()
    {
        var input = """{"intentConfidence":"yüksek","selectedAgent":"X"}""";
        var plan = PlanningResultParser.TryParse(input);
        plan.Should().NotBeNull();
        plan!.IntentConfidence.Should().BeGreaterThan(0.5);
    }

    [Fact]
    public void TryParse_ConfidenceClampedToValidRange()
    {
        var input = """{"intentConfidence":2.5,"selectedAgent":"X"}""";
        var plan = PlanningResultParser.TryParse(input);
        plan.Should().NotBeNull();
        plan!.IntentConfidence.Should().Be(1.0);
    }

    [Fact]
    public void TryParse_NegativeConfidence_ClampedToZero()
    {
        var input = """{"intentConfidence":-0.5,"selectedAgent":"X"}""";
        var plan = PlanningResultParser.TryParse(input);
        plan.Should().NotBeNull();
        plan!.IntentConfidence.Should().Be(0.0);
    }

    [Fact]
    public void TryParse_ClarificationNeeded_FlagSet()
    {
        var input = """
            {"selectedAgent":"X","needsClarification":true,"clarificationQuestion":"Hangi siparişi kastediyorsunuz?"}
            """;

        var plan = PlanningResultParser.TryParse(input);

        plan.Should().NotBeNull();
        plan!.NeedsClarification.Should().BeTrue();
        plan.ClarificationQuestion.Should().Contain("Hangi siparişi");
    }

    [Fact]
    public void TryParse_AlternativesRejected_Parsed()
    {
        var input = """
            {
                "selectedAgent": "OrderInquiryAgent",
                "alternativesRejected": [
                    {"agent": "ComplaintAgent", "reason": "no complaint signal"},
                    {"agent": "ProductInquiryAgent", "reason": "no product context"}
                ]
            }
            """;

        var plan = PlanningResultParser.TryParse(input);

        plan.Should().NotBeNull();
        plan!.AlternativesRejected.Should().HaveCount(2);
        plan.AlternativesRejected[0].Agent.Should().Be("ComplaintAgent");
    }

    [Fact]
    public void TryParse_SupportingEvidence_StringArrayParsed()
    {
        var input = """
            {"selectedAgent":"X","supportingEvidence":["evidence 1","evidence 2"]}
            """;

        var plan = PlanningResultParser.TryParse(input);

        plan.Should().NotBeNull();
        plan!.SupportingEvidence.Should().HaveCount(2);
        plan.SupportingEvidence.Should().Contain("evidence 1");
    }

    [Fact]
    public void TryParse_DefaultConfidenceIsHalf()
    {
        var input = """{"selectedAgent":"X"}""";
        var plan = PlanningResultParser.TryParse(input);
        plan.Should().NotBeNull();
        plan!.IntentConfidence.Should().Be(0.5);
    }
}

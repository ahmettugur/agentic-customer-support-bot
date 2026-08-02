// Tests/Services/PlanningResultParserTests.cs

using CustomerSupportBot.Domain.Services;

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
                "selectedAgent": "OrderAgent",
                "rationale": "User asked about order status",
                "needsClarification": false
            }
            ```
            """;

        var plan = PlanningResultParser.TryParse(input);

        plan.Should().NotBeNull();
        plan.SelectedAgent.Should().Be("OrderAgent");
        plan.NeedsClarification.Should().BeFalse();
    }

    [Fact]
    public void TryParse_PlainJson_ParsesCorrectly()
    {
        var input = """
            {"selectedAgent":"ProductAgent","taskDescription":"Ürün listesini getir"}
            """;

        var plan = PlanningResultParser.TryParse(input);

        plan.Should().NotBeNull();
        plan.SelectedAgent.Should().Be("ProductAgent");
        plan.TaskDescription.Should().Be("Ürün listesini getir");
    }

    [Fact]
    public void TryParse_LegacyIntentFields_Ignored()
    {
        // Intent'in tek sahibi ReasoningService — eski şemadan kalan
        // detectedIntent/intentConfidence alanları parse edilmez, sessizce yok sayılır.
        var input = """
            {"selectedAgent":"OrderAgent","detectedIntent":"order_inquiry","intentConfidence":0.85}
            """;

        var plan = PlanningResultParser.TryParse(input);

        plan.Should().NotBeNull();
        plan.SelectedAgent.Should().Be("OrderAgent");
    }

    [Fact]
    public void TryParse_ClarificationNeeded_FlagSet()
    {
        var input = """
            {"selectedAgent":"X","needsClarification":true,"clarificationQuestion":"Hangi siparişi kastediyorsunuz?"}
            """;

        var plan = PlanningResultParser.TryParse(input);

        plan.Should().NotBeNull();
        plan.NeedsClarification.Should().BeTrue();
        plan.ClarificationQuestion.Should().Contain("Hangi siparişi");
    }

    [Fact]
    public void TryParse_AlternativesRejected_Parsed()
    {
        var input = """
            {
                "selectedAgent": "OrderAgent",
                "alternativesRejected": [
                    {"agent": "ComplaintAgent", "reason": "no complaint signal"},
                    {"agent": "ProductAgent", "reason": "no product context"}
                ]
            }
            """;

        var plan = PlanningResultParser.TryParse(input);

        plan.Should().NotBeNull();
        plan.AlternativesRejected.Should().HaveCount(2);
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
        plan.SupportingEvidence.Should().HaveCount(2);
        plan.SupportingEvidence.Should().Contain("evidence 1");
    }
}

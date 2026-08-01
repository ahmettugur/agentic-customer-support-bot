// Tests/Agents/WorkflowResponseExtractorTests.cs
using CustomerSupportBot.Adapters.Agents;
using CustomerSupportBot.Application.Ports.Inbound;

namespace CustomerSupportBot.Api.Tests.Agents;

public class WorkflowResponseExtractorTests
{
    [Fact]
    public void RemoveTerminationMarkers_NoMarker_ReturnsUnchanged()
    {
        WorkflowResponseExtractor.RemoveTerminationMarkers("Hello user")
            .Should().Be("Hello user");
    }

    [Fact]
    public void RemoveTerminationMarkers_WithReason_StripsTail()
    {
        var input = "Yanıtınız hazır.\nTERMINATE: reason=success";
        WorkflowResponseExtractor.RemoveTerminationMarkers(input)
            .Should().Be("Yanıtınız hazır.");
    }

    [Fact]
    public void RemoveTerminationMarkers_ParenForm_StripsTail()
    {
        var input = "Tamamlandı. TERMINATE (success)";
        WorkflowResponseExtractor.RemoveTerminationMarkers(input)
            .Should().Be("Tamamlandı.");
    }

    [Fact]
    public void RemoveTerminationMarkers_BareTerminate_StripsTail()
    {
        WorkflowResponseExtractor.RemoveTerminationMarkers("Bitti TERMINATE her şey iyi")
            .Should().Be("Bitti");
    }

    [Fact]
    public void RemoveTerminationMarkers_EmptyInput_ReturnsEmpty()
    {
        WorkflowResponseExtractor.RemoveTerminationMarkers("").Should().Be("");
    }

    [Fact]
    public void RemoveTechnicalJsonBlocks_FencedJson_Stripped()
    {
        var input = """
            Hello user.
            ```json
            {"preToolCheck": {"canProceed": true}}
            ```
            Done.
            """;

        var result = WorkflowResponseExtractor.RemoveTechnicalJsonBlocks(input);
        result.Should().NotContain("preToolCheck");
        result.Should().Contain("Hello user");
        result.Should().Contain("Done");
    }

    [Fact]
    public void RemoveTechnicalJsonBlocks_NoTechnical_Unchanged()
    {
        var input = "Plain user response without JSON.";
        WorkflowResponseExtractor.RemoveTechnicalJsonBlocks(input).Should().Be(input);
    }

    [Fact]
    public void ContainsAgentRoutingMessage_AgentName_True()
    {
        WorkflowResponseExtractor.ContainsAgentRoutingMessage(
            "OrderAgent: lütfen sipariş numaranızı verin").Should().BeTrue();
    }

    [Fact]
    public void ContainsAgentRoutingMessage_NoAgentName_False()
    {
        WorkflowResponseExtractor.ContainsAgentRoutingMessage(
            "Merhaba, size nasıl yardımcı olabilirim?").Should().BeFalse();
    }

    [Fact]
    public void ParseTerminationReasonFromResult_ReasonValue_ReturnsLowercase()
    {
        WorkflowResponseExtractor.ParseTerminationReasonFromResult(
            "TERMINATE: reason=Success").Should().Be("success");
    }

    [Fact]
    public void ParseTerminationReasonFromResult_ParenForm_Returns()
    {
        WorkflowResponseExtractor.ParseTerminationReasonFromResult(
            "TERMINATE (awaiting_user_input)").Should().Be("awaiting_user_input");
    }

    [Fact]
    public void ParseTerminationReasonFromResult_NoReason_ReturnsNull()
    {
        WorkflowResponseExtractor.ParseTerminationReasonFromResult("Plain text").Should().BeNull();
    }

    [Fact]
    public void ExtractDeltaText_NullData_ReturnsEmpty()
    {
        WorkflowResponseExtractor.ExtractDeltaText(null).Should().Be("");
    }

    [Fact]
    public void ExtractDeltaText_TextDeltaPayload_ReadsText()
    {
        var data = new TextDeltaPayload("hello");
        WorkflowResponseExtractor.ExtractDeltaText(data).Should().Be("hello");
    }

    [Fact]
    public void IsInternalWorkflowExecutor_EmptyId_True()
    {
        WorkflowResponseExtractor.IsInternalWorkflowExecutor("").Should().BeTrue();
    }
}

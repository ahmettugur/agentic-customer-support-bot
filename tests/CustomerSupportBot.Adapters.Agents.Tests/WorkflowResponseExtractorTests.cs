// Tests/Agents/WorkflowResponseExtractorTests.cs
using CustomerSupportBot.Adapters.Agents;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Adapters.Agents.Tests;

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
    public void RemoveTerminationMarkers_InlineParenForm_IsOrdinaryText()
    {
        var input = "Tamamlandı. TERMINATE (success)";
        WorkflowResponseExtractor.RemoveTerminationMarkers(input)
            .Should().Be(input);
    }

    [Fact]
    public void RemoveTerminationMarkers_BareTerminate_IsOrdinaryText()
    {
        WorkflowResponseExtractor.RemoveTerminationMarkers("Bitti TERMINATE her şey iyi")
            .Should().Be("Bitti TERMINATE her şey iyi");
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
            "TERMINATE: reason=Completed").Should().Be("completed");
    }

    [Fact]
    public void ParseTerminationReasonFromResult_ParenForm_IsNotProtocol()
    {
        WorkflowResponseExtractor.ParseTerminationReasonFromResult(
            "TERMINATE (awaiting_user_input)").Should().BeNull();
    }

    [Fact]
    public void ParseTerminationReasonFromResult_NoReason_ReturnsNull()
    {
        WorkflowResponseExtractor.ParseTerminationReasonFromResult("Plain text").Should().BeNull();
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("awaiting_user_input")]
    [InlineData("escalation_needed")]
    [InlineData("not_found")]
    [InlineData("error")]
    [InlineData("max_messages_reached")]
    [InlineData("repeated_tool_call_guard")]
    [InlineData("timeout")]
    public void ParseTerminationReasonFromResult_KnownReasons_ReturnCanonical(string reason)
    {
        WorkflowResponseExtractor.ParseTerminationReasonFromResult(
            $"TERMINATE: reason={reason}").Should().Be(reason);
    }

    [Theory]
    [InlineData("COMPLETED", "completed")]
    [InlineData("Escalation_Needed", "escalation_needed")]
    [InlineData("AWAITING_USER_INPUT", "awaiting_user_input")]
    public void ParseTerminationReasonFromResult_CaseVariations_Normalized(string raw, string expected)
    {
        WorkflowResponseExtractor.ParseTerminationReasonFromResult(
            $"TERMINATE: reason={raw}").Should().Be(expected);
    }

    [Fact]
    public void ParseTerminationReasonFromResult_UnknownReason_ReturnsNull()
    {
        // Bilinmeyen reason → null; çağıran taraf ReasonCompleted fallback'ini uygular.
        WorkflowResponseExtractor.ParseTerminationReasonFromResult(
            "TERMINATE: reason=success").Should().BeNull();
    }

    [Fact]
    public void ParseTerminationReasonFromResult_UnknownReasonParenForm_ReturnsNull()
    {
        WorkflowResponseExtractor.ParseTerminationReasonFromResult(
            "TERMINATE (bottan_sikildim)").Should().BeNull();
    }

    [Fact]
    public void ParseTerminationReasonFromResult_EmptyInput_ReturnsNull()
    {
        WorkflowResponseExtractor.ParseTerminationReasonFromResult("").Should().BeNull();
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

    [Fact]
    public void ContainsHumanHandoffToolCall_ToolCalled_True()
    {
        var msg = new ChatMessage(ChatRole.Assistant, new List<AIContent>
        {
            new FunctionCallContent("call1", WellKnown.ToolNames.HumanHandoff,
                new Dictionary<string, object?> { ["reason"] = "bottan sıkıldım" })
        });

        WorkflowResponseExtractor.ContainsHumanHandoffToolCall([msg]).Should().BeTrue();
    }

    [Fact]
    public void ContainsHumanHandoffToolCall_DifferentTool_False()
    {
        var msg = new ChatMessage(ChatRole.Assistant, new List<AIContent>
        {
            new FunctionCallContent("call1", WellKnown.ToolNames.OrderStatus,
                new Dictionary<string, object?> { ["orderId"] = "1030" })
        });

        WorkflowResponseExtractor.ContainsHumanHandoffToolCall([msg]).Should().BeFalse();
    }

    [Fact]
    public void ContainsHumanHandoffToolCall_NoFunctionCalls_False()
    {
        var msg = new ChatMessage(ChatRole.Assistant, "sadece metin");
        WorkflowResponseExtractor.ContainsHumanHandoffToolCall([msg]).Should().BeFalse();
    }
}

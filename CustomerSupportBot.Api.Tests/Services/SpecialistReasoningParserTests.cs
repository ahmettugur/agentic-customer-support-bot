// Tests/Services/SpecialistReasoningParserTests.cs

using CustomerSupportBot.Api.Models;
using CustomerSupportBot.Api.Services;
using CustomerSupportBot.Api.Models;
using CustomerSupportBot.Api.Services;
using FluentAssertions;

namespace CustomerSupportBot.Api.Tests.Services;

public class SpecialistReasoningParserTests
{
    [Fact]
    public void TryParse_Null_ReturnsNull() =>
        SpecialistReasoningParser.TryParse(null, "X").Should().BeNull();

    [Fact]
    public void TryParse_NoJson_ReturnsNull() =>
        SpecialistReasoningParser.TryParse("plain text", "X").Should().BeNull();

    [Fact]
    public void TryParse_JsonWithoutSpecialistKeys_ReturnsNull()
    {
        var input = """{"selectedAgent":"X","intentConfidence":0.8}""";
        SpecialistReasoningParser.TryParse(input, "X").Should().BeNull();
    }

    [Fact]
    public void TryParse_PreToolCheck_Parsed()
    {
        var input = """
            {
                "preToolCheck": {
                    "requiredParams": ["customerId","productId"],
                    "collectedParams": ["customerId"],
                    "missingParams": ["productId"],
                    "canProceed": false,
                    "confidence": 0.7,
                    "reasoning": "missing productId"
                }
            }
            """;

        var result = SpecialistReasoningParser.TryParse(input, "OrderPlacementAgent");

        result.Should().NotBeNull();
        result!.AgentName.Should().Be("OrderPlacementAgent");
        result.PreToolCheck.Should().NotBeNull();
        result.PreToolCheck!.CanProceed.Should().BeFalse();
        result.PreToolCheck.MissingParams.Should().Contain("productId");
        result.PreToolCheck.Confidence.Should().Be(0.7);
    }

    [Fact]
    public void TryParse_PostToolReflection_DoneStatus()
    {
        var input = """
            {
                "resultConfidence": 0.95,
                "resultNotes": "found",
                "postToolReflection": {
                    "taskComplete": true,
                    "status": "done",
                    "summary": "Order ORD-1 delivered"
                }
            }
            """;

        var result = SpecialistReasoningParser.TryParse(input, "OrderInquiryAgent");

        result.Should().NotBeNull();
        result!.ResultConfidence.Should().Be(0.95);
        result.PostToolReflection.Should().NotBeNull();
        result.PostToolReflection!.TaskComplete.Should().BeTrue();
        result.PostToolReflection.StatusEnum.Should().Be(TaskCompletionStatus.Done);
    }

    [Fact]
    public void TryParse_PostToolReflection_NeedsEscalation()
    {
        var input = """
            {"postToolReflection":{"status":"needs_escalation","summary":"customer angry"}}
            """;

        var result = SpecialistReasoningParser.TryParse(input, "ComplaintAgent");

        result.Should().NotBeNull();
        result!.PostToolReflection!.StatusEnum.Should().Be(TaskCompletionStatus.NeedsEscalation);
    }

    [Fact]
    public void TryParse_HandoffSuggestion_NullOrNoneStripped()
    {
        var input = """
            {"postToolReflection":{"status":"done","handoffSuggestion":"none"}}
            """;

        var result = SpecialistReasoningParser.TryParse(input, "X");
        result!.PostToolReflection!.HandoffSuggestion.Should().BeNull();
    }

    [Fact]
    public void TryParse_HandoffSuggestion_AgentNamePreserved()
    {
        var input = """
            {"postToolReflection":{"handoffSuggestion":"OrderPlacementAgent","handoffReason":"order needed"}}
            """;

        var result = SpecialistReasoningParser.TryParse(input, "ProductInquiryAgent");

        result!.PostToolReflection!.HandoffSuggestion.Should().Be("OrderPlacementAgent");
        result.PostToolReflection.HandoffReason.Should().Be("order needed");
    }

    [Fact]
    public void TryParse_StatusAlias_NormalizedToCanonical()
    {
        var input = """{"postToolReflection":{"status":"completed"}}""";
        var result = SpecialistReasoningParser.TryParse(input, "X");
        result!.PostToolReflection!.Status.Should().Be(WellKnown.TaskStatuses.Done);
    }

    [Fact]
    public void TryParse_UnknownStatus_DefaultsToDone()
    {
        var input = """{"postToolReflection":{"status":"weird_status"}}""";
        var result = SpecialistReasoningParser.TryParse(input, "X");
        result!.PostToolReflection!.StatusEnum.Should().Be(TaskCompletionStatus.Done);
    }

    [Fact]
    public void TryParse_ResultConfidenceClamped()
    {
        var input = """{"resultConfidence":2.5}""";
        var result = SpecialistReasoningParser.TryParse(input, "X");
        result.Should().NotBeNull();
        result!.ResultConfidence.Should().Be(1.0);
    }
}

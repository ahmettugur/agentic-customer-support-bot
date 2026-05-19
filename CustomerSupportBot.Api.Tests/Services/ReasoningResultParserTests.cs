// Tests/Services/ReasoningResultParserTests.cs

using CustomerSupportBot.Domain.Services;

namespace CustomerSupportBot.Api.Tests.Services;

public class ReasoningResultParserTests
{
    [Fact]
    public void Parse_PlainText_FallsBackToAnalysis()
    {
        var result = ReasoningResultParser.Parse("kullanıcı sipariş soruyor");
        result.Should().NotBeNull();
        result.Analysis.Should().Contain("kullanıcı");
        result.ConfidenceScore.Should().BeApproximately(0.3, 0.01);
    }

    [Fact]
    public void Parse_ValidJson_AllFieldsExtracted()
    {
        var input = """
            ```json
            {
                "analysis": "Kullanıcı sipariş durumu sormuş.",
                "intent": "sipariş_sorgulama",
                "confidenceScore": 0.92,
                "rationale": "ORD-1 mevcut",
                "nextAction": "OrderAgent'e yönlendir",
                "requiredInfo": []
            }
            ```
            """;

        var result = ReasoningResultParser.Parse(input);
        result.Analysis.Should().Be("Kullanıcı sipariş durumu sormuş.");
        result.Intent.Should().Be("sipariş_sorgulama");
        result.ConfidenceScore.Should().Be(0.92);
        result.NextAction.Should().Contain("Order");
    }

    [Fact]
    public void Parse_ConfidenceScoreClampedHigh()
    {
        var input = """{"confidenceScore": 5.0, "analysis": "x"}""";
        var result = ReasoningResultParser.Parse(input);
        result.ConfidenceScore.Should().Be(1.0);
    }

    [Fact]
    public void Parse_ConfidenceScoreClampedLow()
    {
        var input = """{"confidenceScore": -1.0, "analysis": "x"}""";
        var result = ReasoningResultParser.Parse(input);
        result.ConfidenceScore.Should().Be(0.0);
    }

    [Fact]
    public void Parse_ConfidenceStringFallback_Score()
    {
        var input = """{"confidence":"yüksek","analysis":"x"}""";
        var result = ReasoningResultParser.Parse(input);
        result.ConfidenceScore.Should().BeGreaterThan(0.7);
    }

    [Fact]
    public void Parse_AssumptionsArray_Parsed()
    {
        var input = """
            {"analysis":"x","assumptions":["aktif müşteri","türkçe konuşuyor"]}
            """;
        var result = ReasoningResultParser.Parse(input);
        result.Assumptions.Should().HaveCount(2);
    }

    [Fact]
    public void SanitizeAnalysis_FenceMarkers_Stripped()
    {
        ReasoningResultParser.SanitizeAnalysis("```json\nhello\n```")
            .Should().NotContain("```");
    }

    [Fact]
    public void SanitizeAnalysis_TooLong_Truncated()
    {
        var raw = new string('x', 1000);
        var s = ReasoningResultParser.SanitizeAnalysis(raw);
        s.Length.Should().BeLessThan(raw.Length);
        s.Should().EndWith("…");
    }

    [Fact]
    public void SanitizeAnalysis_Empty_ReturnsEmpty()
    {
        ReasoningResultParser.SanitizeAnalysis("").Should().Be("");
    }

    [Fact]
    public void ExtractJson_Fenced_StripsFence()
    {
        var input = "```json\n{\"a\":1}\n```";
        ReasoningResultParser.ExtractJson(input).Should().Contain("\"a\":1");
    }

    [Theory]
    [InlineData(0.05, "angry")]
    [InlineData(0.25, "negative")]
    [InlineData(0.5, "neutral")]
    [InlineData(0.9, "positive")]
    public void SentimentScoreToLabel_Bounds(double score, string label)
    {
        ReasoningResultParser.SentimentScoreToLabel(score).Should().Be(label);
    }
}

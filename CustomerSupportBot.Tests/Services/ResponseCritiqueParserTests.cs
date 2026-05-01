// Tests/Services/ResponseCritiqueParserTests.cs
using CustomerSupportBot.Services;
using FluentAssertions;

namespace CustomerSupportBot.Tests.Services;

public class ResponseCritiqueParserTests
{
    [Fact]
    public void TryParse_Null_ReturnsNull()
    {
        ResponseCritiqueParser.TryParse(null).Should().BeNull();
    }

    [Fact]
    public void TryParse_NoCritiqueJson_ReturnsNull()
    {
        ResponseCritiqueParser.TryParse("Plain response, no JSON").Should().BeNull();
    }

    [Fact]
    public void TryParse_FencedSelfCritique_Parsed()
    {
        var input = """
            Yanıtınız hazır.
            ```json
            {
                "selfCritique": {
                    "addressesUserQuery": true,
                    "tone": "appropriate",
                    "completeness": 0.85,
                    "hallucinationRisk": 0.1,
                    "issuesFound": ["minor typo"],
                    "revisionNeeded": false
                }
            }
            ```
            """;

        var crit = ResponseCritiqueParser.TryParse(input);
        crit.Should().NotBeNull();
        crit!.AddressesUserQuery.Should().BeTrue();
        crit.Completeness.Should().Be(0.85);
        crit.HallucinationRisk.Should().Be(0.1);
        crit.IssuesFound.Should().ContainSingle().Which.Should().Be("minor typo");
        crit.RevisionNeeded.Should().BeFalse();
    }

    [Fact]
    public void TryParse_FlatCritiqueJson_AlsoParsed()
    {
        var input = """
            ```json
            {"addressesUserQuery": false, "completeness": 0.5, "revisionNeeded": true, "revisionNotes": "missing details"}
            ```
            """;

        var crit = ResponseCritiqueParser.TryParse(input);
        crit.Should().NotBeNull();
        crit!.AddressesUserQuery.Should().BeFalse();
        crit.RevisionNeeded.Should().BeTrue();
        crit.RevisionNotes.Should().Be("missing details");
    }

    [Fact]
    public void TryParse_InlineJsonWithoutFence_Parsed()
    {
        var input = """Yanıt içeriği {"selfCritique":{"addressesUserQuery":true,"completeness":1.0}} buradadır.""";
        var crit = ResponseCritiqueParser.TryParse(input);
        crit.Should().NotBeNull();
        crit!.Completeness.Should().Be(1.0);
    }

    [Fact]
    public void TryParse_MalformedJson_ReturnsNull()
    {
        ResponseCritiqueParser.TryParse("```json\n{\"selfCritique\":\n```").Should().BeNull();
    }
}

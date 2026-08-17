// Tests/Services/SelfCritiqueParserTests.cs
// ResponseAgent çıktısının SONUNDAKİ selfCritique bloğunun çıkarılması.
// Diğer parser'lardan farkı: blok en başta değil en sonda, üstelik kullanıcı yanıtı
// da kod bloğu/parantez içerebilir — bu yüzden "ilk fence" mantığı burada çalışmaz.

using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;

namespace CustomerSupportBot.Domain.Tests;

public class SelfCritiqueParserTests
{
    private const string FullOutput = """
        Tabii, 1030 numaralı siparişiniz kargoya verildi.
        TERMINATE: reason=completed
        ```json
        {"selfCritique": {"addressesUserQuery": true, "tone": "appropriate", "completeness": 0.95,
         "hallucinationRisk": 0.0, "sources": ["OrderAgent.resultNotes"], "issuesFound": [],
         "revisionNeeded": false, "revisionNotes": ""}}
        ```
        """;

    [Fact]
    public void TryParse_FullResponseAgentOutput_ExtractsAllFields()
    {
        var c = SelfCritiqueParser.TryParse(FullOutput);

        c.Should().NotBeNull();
        c!.AddressesUserQuery.Should().BeTrue();
        c.Tone.Should().Be(WellKnown.CritiqueTones.Appropriate);
        c.Completeness.Should().BeApproximately(0.95, 0.001);
        c.HallucinationRisk.Should().Be(0.0);
        c.Sources.Should().ContainSingle().Which.Should().Be("OrderAgent.resultNotes");
        c.RevisionNeeded.Should().BeFalse();
        c.RevisionNotes.Should().BeNull("boş string null'a normalize edilir");
    }

    [Fact]
    public void TryParse_NoCritiqueBlock_ReturnsNull()
    {
        SelfCritiqueParser.TryParse("Siparişiniz kargoda.\nTERMINATE: reason=completed")
            .Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_EmptyInput_ReturnsNull(string? input)
        => SelfCritiqueParser.TryParse(input).Should().BeNull();

    [Fact]
    public void TryParse_MalformedJson_ReturnsNullWithoutThrowing()
    {
        var act = () => SelfCritiqueParser.TryParse("""{"selfCritique": {"tone": }}""");
        act.Should().NotThrow();
        act().Should().BeNull();
    }

    [Fact]
    public void TryParse_BraceInsideStringValue_DoesNotBreakBraceMatching()
    {
        // revisionNotes serbest metin — içindeki '}' parantez eşlemesini bozmamalı.
        var output = """
            Yanıt metni.
            ```json
            {"selfCritique": {"tone": "robotic", "revisionNotes": "şablon } kullanılmış {", "completeness": 0.4}}
            ```
            """;

        var c = SelfCritiqueParser.TryParse(output);

        c.Should().NotBeNull();
        c!.RevisionNotes.Should().Be("şablon } kullanılmış {");
        c.Completeness.Should().BeApproximately(0.4, 0.001);
    }

    [Fact]
    public void TryParse_UserAnswerContainsCodeBlock_StillFindsCritique()
    {
        // "İlk fence"i alan bir parser burada kullanıcı yanıtındaki bloğu seçip başarısız olurdu.
        var output = """
            İşte örnek: ```json {"orderId": "1030"}``` şeklinde görünür.
            TERMINATE: reason=completed
            ```json
            {"selfCritique": {"tone": "appropriate", "completeness": 1.0, "hallucinationRisk": 0.1}}
            ```
            """;

        var c = SelfCritiqueParser.TryParse(output);

        c.Should().NotBeNull();
        c!.HallucinationRisk.Should().BeApproximately(0.1, 0.001);
    }

    // ── IsConcerning: LessonMiner'ın inceleme adayı seçimi buna bağlı ──────────────

    [Fact]
    public void IsConcerning_CleanCritique_IsFalse()
        => SelfCritiqueParser.TryParse(FullOutput)!.IsConcerning.Should().BeFalse();

    [Theory]
    [InlineData("""{"selfCritique":{"revisionNeeded":true}}""", "model düzeltme gerektiğini söyledi")]
    [InlineData("""{"selfCritique":{"addressesUserQuery":false}}""", "soru yanıtlanmadı")]
    [InlineData("""{"selfCritique":{"hallucinationRisk":0.5}}""", "halüsinasyon riski eşikte")]
    [InlineData("""{"selfCritique":{"completeness":0.69}}""", "tamlık eşiğin altında")]
    [InlineData("""{"selfCritique":{"tone":"robotic"}}""", "robotik ton")]
    [InlineData("""{"selfCritique":{"tone":"impolite"}}""", "kaba ton")]
    public void IsConcerning_FlagsEachProblemSignal(string json, string why)
        => SelfCritiqueParser.TryParse(json)!.IsConcerning.Should().BeTrue(why);

    [Fact]
    public void IsConcerning_ThresholdsMatchPromptRules()
    {
        // Prompt "hallucinationRisk ≥ 0.5" ve "completeness < 0.7" diyor — sınırın doğru
        // tarafındaki değerler bayrak KALDIRMAMALI (off-by-one koruması).
        SelfCritiqueParser.TryParse("""{"selfCritique":{"hallucinationRisk":0.49}}""")!
            .IsConcerning.Should().BeFalse();
        SelfCritiqueParser.TryParse("""{"selfCritique":{"completeness":0.7}}""")!
            .IsConcerning.Should().BeFalse();
    }
}

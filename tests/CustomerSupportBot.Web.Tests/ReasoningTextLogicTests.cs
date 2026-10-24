// Düşünce süreci paneli: sunucu artık düz analiz metni akıtıyor; eski sunucu ham JSON akıtıyordu. İstemci ikisini de doğru gösterir.

using CustomerSupportBot.Web.Services;

namespace CustomerSupportBot.Web.Tests;

public class ReasoningTextLogicTests
{
    [Fact]
    public void PlainText_IsShownAsIs() =>
        ReasoningTextLogic.PartialAnalysis("Kullanıcı siparişini soruyor").Should().Be("Kullanıcı siparişini soruyor");

    [Fact]
    public void LegacyRawJson_StillYieldsOnlyTheAnalysis() =>
        ReasoningTextLogic.PartialAnalysis("{\"analysis\":\"Kullanıcı siparişini soruyor\",\"steps\":[").Should().Be("Kullanıcı siparişini soruyor");

    [Fact]
    public void LegacyPartialJson_ShowsTheTextSoFar() =>
        ReasoningTextLogic.PartialAnalysis("{\"analysis\":\"Kullanıcı sipar").Should().Be("Kullanıcı sipar");

    [Fact]
    public void LegacyJsonBeforeTheAnalysisKey_ShowsNothingYet()
    {
        ReasoningTextLogic.PartialAnalysis("{\"int").Should().BeEmpty();
        ReasoningTextLogic.PartialAnalysis("{\"intent\":\"genel\"").Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void NothingYet_IsEmpty(string? raw) => ReasoningTextLogic.PartialAnalysis(raw).Should().BeEmpty();
}

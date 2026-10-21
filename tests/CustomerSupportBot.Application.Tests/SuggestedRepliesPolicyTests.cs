// Hızlı yanıt çipleri: tur bittikten sonra niyete göre sabit, kural tabanlı öneriler (ek LLM çağrısı yok).
// Çipin metni müşterinin bir sonraki mesajı olarak aynen gönderilir; bu yüzden küçük, kapalı ve bilinen bir kümedir.

using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Tests;

public class SuggestedRepliesPolicyTests
{
    private const string Completed = WellKnown.Termination.ReasonCompleted;
    private const string Statement = "Siparişiniz kargoya verildi.";

    [Theory]
    [InlineData(WellKnown.Intents.OrderInquiry)]
    [InlineData(WellKnown.Intents.OrderListing)]
    public void AfterAnOrderLookup_OffersCancelReturnAndComplaint(string intent)
    {
        var s = SuggestedRepliesPolicy.Suggest(intent, Completed, Statement, isCompound: false);

        s.Should().Contain(["Siparişimi iptal etmek istiyorum", "İade talebi oluşturmak istiyorum", "Siparişimle ilgili şikayetim var"]);
    }

    [Fact]
    public void AfterProductInfo_OffersOrdering() =>
        SuggestedRepliesPolicy.Suggest(WellKnown.Intents.ProductInfo, Completed, Statement, false)
            .Should().Contain("Sipariş vermek istiyorum");

    [Theory]
    [InlineData(WellKnown.Intents.OrderCreation)]
    [InlineData(WellKnown.Intents.OrderCancellation)]
    [InlineData(WellKnown.Intents.ReturnRequest)]
    [InlineData(WellKnown.Intents.Complaint)]
    public void AfterAnAction_OffersStatusCheckAndAHuman(string intent)
    {
        var s = SuggestedRepliesPolicy.Suggest(intent, Completed, Statement, false);

        s.Should().Contain("Temsilciyle görüşmek istiyorum");
        s.Should().HaveCountGreaterThan(1);
    }

    [Theory]
    [InlineData(WellKnown.Intents.General)]
    [InlineData(WellKnown.Intents.Unknown)]
    [InlineData(WellKnown.Intents.HumanHandoffRequest)]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("hiç duyulmamış niyet")]
    public void WithoutAKnownActionableIntent_OffersNothing(string? intent) =>
        SuggestedRepliesPolicy.Suggest(intent, Completed, Statement, false).Should().BeEmpty();

    [Theory]
    [InlineData(WellKnown.Termination.ReasonAwaitingUserInput)]
    [InlineData(WellKnown.Termination.ReasonEscalationNeeded)]
    [InlineData(WellKnown.Termination.ReasonNotFound)]
    [InlineData(WellKnown.Termination.ReasonError)]
    [InlineData("timeout")]
    [InlineData(null)]
    public void OnlyACompletedTurnGetsSuggestions(string? termination) =>
        SuggestedRepliesPolicy.Suggest(WellKnown.Intents.OrderInquiry, termination, Statement, false).Should().BeEmpty();

    [Theory]
    [InlineData("Hangi siparişten bahsediyorsunuz?")]
    [InlineData("Sipariş numaranızı paylaşır mısınız?  ")]
    public void WhenTheBotAsksAQuestion_OffersNothing_SoTheCustomerAnswersIt(string response) =>
        SuggestedRepliesPolicy.Suggest(WellKnown.Intents.OrderInquiry, Completed, response, false).Should().BeEmpty();

    [Fact]
    public void ACompoundTurn_OffersNothing_BecauseItMixesSeveralIntents() =>
        SuggestedRepliesPolicy.Suggest(WellKnown.Intents.OrderInquiry, Completed, Statement, isCompound: true).Should().BeEmpty();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WithAnEmptyAnswer_OffersNothing(string response) =>
        SuggestedRepliesPolicy.Suggest(WellKnown.Intents.OrderInquiry, Completed, response, false).Should().BeEmpty();

    [Fact]
    public void EverySuggestion_IsShort_AndAtMostThreeAreOffered()
    {
        var all = new[]
        {
            WellKnown.Intents.OrderInquiry, WellKnown.Intents.OrderListing, WellKnown.Intents.ProductInfo, WellKnown.Intents.OrderCreation,
            WellKnown.Intents.OrderCancellation, WellKnown.Intents.ReturnRequest, WellKnown.Intents.Complaint
        }.Select(i => SuggestedRepliesPolicy.Suggest(i, Completed, Statement, false)).ToList();

        all.Should().OnlyContain(s => s.Count >= 1 && s.Count <= 3);
        all.SelectMany(s => s).Should().OnlyContain(text => text.Length <= 60);
    }
}

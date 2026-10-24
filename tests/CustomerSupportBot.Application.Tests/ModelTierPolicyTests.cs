// Model yönlendirme politikası (saf): yalnızca basit + yüksek güvenli + yan etkisiz turlar hızlı modele gider.

using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Tests;

public class ModelTierPolicyTests
{
    private static readonly ModelRoutingOptions On = new() { Enabled = true };

    private static ReasoningResult R(string intent, double confidence = 0.9, string sentiment = "neutral", double sentimentScore = 0.5) =>
        new() { Intent = intent, ConfidenceScore = confidence, Sentiment = sentiment, SentimentScore = sentimentScore };

    [Theory]
    [InlineData(WellKnown.Intents.ProductInfo)]
    [InlineData(WellKnown.Intents.OrderInquiry)]
    [InlineData(WellKnown.Intents.OrderListing)]
    [InlineData(WellKnown.Intents.General)]
    public void SimpleConfidentIntents_GoToTheFastModel(string intent) =>
        ModelTierPolicy.Choose(R(intent), isCompound: false, On).Should().Be(ModelTier.Fast);

    [Theory]
    [InlineData(WellKnown.Intents.OrderCreation)]
    [InlineData(WellKnown.Intents.OrderCancellation)]
    [InlineData(WellKnown.Intents.ReturnRequest)]
    [InlineData(WellKnown.Intents.Complaint)]
    [InlineData(WellKnown.Intents.HumanHandoffRequest)]
    public void SideEffectOrSensitiveIntents_NeverGoFast(string intent) =>
        ModelTierPolicy.Choose(R(intent), false, On).Should().Be(ModelTier.Standard);

    [Fact]
    public void LowConfidence_StaysOnTheStandardModel() =>
        ModelTierPolicy.Choose(R(WellKnown.Intents.ProductInfo, 0.6), false, On).Should().Be(ModelTier.Standard);

    [Fact]
    public void TheConfidenceBoundary_IsInclusive() =>
        ModelTierPolicy.Choose(R(WellKnown.Intents.ProductInfo, On.MinConfidence), false, On).Should().Be(ModelTier.Fast);

    [Fact]
    public void CompoundQueries_StayStandard() =>
        ModelTierPolicy.Choose(R(WellKnown.Intents.ProductInfo), isCompound: true, On).Should().Be(ModelTier.Standard);

    [Fact]
    public void AnUpsetCustomer_GetsTheBetterModel() =>
        ModelTierPolicy.Choose(R(WellKnown.Intents.OrderInquiry, sentiment: "angry", sentimentScore: 0.1), false, On)
            .Should().Be(ModelTier.Standard);

    [Fact]
    public void AFallbackReasoning_IsNotTrustedToBeSimple()
    {
        var r = R(WellKnown.Intents.ProductInfo);
        r.IsFallback = true;

        ModelTierPolicy.Choose(r, false, On).Should().Be(ModelTier.Standard);
    }

    [Fact]
    public void WithoutReasoningOrWhenDisabled_StandardIsUsed()
    {
        ModelTierPolicy.Choose(null, false, On).Should().Be(ModelTier.Standard);
        ModelTierPolicy.Choose(R(WellKnown.Intents.ProductInfo), false, new ModelRoutingOptions { Enabled = false }).Should().Be(ModelTier.Standard);
    }

    [Fact]
    public void TheSimpleIntentList_IsConfigurable()
    {
        var o = new ModelRoutingOptions { Enabled = true, FastIntents = [WellKnown.Intents.Complaint] };

        ModelTierPolicy.Choose(R(WellKnown.Intents.Complaint), false, o).Should().Be(ModelTier.Fast);
        ModelTierPolicy.Choose(R(WellKnown.Intents.ProductInfo), false, o).Should().Be(ModelTier.Standard);
    }
}

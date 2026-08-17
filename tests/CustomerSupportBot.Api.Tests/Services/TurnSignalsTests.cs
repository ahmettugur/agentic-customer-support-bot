// Tests/Services/TurnSignalsTests.cs
//
// TurnSignals.From, "LLM gerçekten bir sinyal üretti mi" kararının tek yeridir.
// Yanlış pozitif verirse (ör. parser'ın fallback değerlerini gerçek karar sayarsa)
// kural tabanlı çıkarım hiç devreye giremez ve oturumun niyeti "bilinmiyor"a düşer.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Api.Tests.Services;

public class TurnSignalsTests
{
    [Fact]
    public void From_Null_ReturnsNull() => TurnSignals.From(null).Should().BeNull();

    [Fact]
    public void From_UnknownIntent_IsTreatedAsNoSignal()
    {
        // ReasoningResultParser JSON'u okuyamadığında Intent'e bu değeri koyar —
        // bir karar değil, bir başarısızlık işareti.
        var signals = TurnSignals.From(new ReasoningResult { Intent = WellKnown.Intents.Unknown });

        signals.Should().BeNull();
    }

    [Fact]
    public void From_DefaultNeutralSentiment_IsTreatedAsNoSignal()
    {
        // neutral@0.5, parser'ın alan hiç dönmediğinde ürettiği varsayılan çift.
        var signals = TurnSignals.From(new ReasoningResult
        {
            Sentiment = WellKnown.Sentiments.Neutral,
            SentimentScore = 0.5
        });

        signals.Should().BeNull();
    }

    [Fact]
    public void From_ExplicitNeutralWithDistinctScore_IsASignal()
    {
        var signals = TurnSignals.From(new ReasoningResult
        {
            Sentiment = WellKnown.Sentiments.Neutral,
            SentimentScore = 0.55
        });

        signals.Should().NotBeNull();
        signals!.SentimentLabel.Should().Be(WellKnown.Sentiments.Neutral);
        signals.SentimentScore.Should().Be(0.55);
    }

    [Fact]
    public void From_IntentOnly_LeavesSentimentNull()
    {
        var signals = TurnSignals.From(new ReasoningResult
        {
            Intent = WellKnown.Intents.ReturnRequest,
            Sentiment = WellKnown.Sentiments.Neutral,
            SentimentScore = 0.5
        });

        signals.Should().NotBeNull();
        signals!.Intent.Should().Be(WellKnown.Intents.ReturnRequest);
        signals.SentimentLabel.Should().BeNull("kural tabanlı sentiment devreye girmeli");
        signals.SentimentScore.Should().BeNull();
    }

    [Fact]
    public void From_SentimentOnly_LeavesIntentNull()
    {
        var signals = TurnSignals.From(new ReasoningResult
        {
            Intent = "",
            Sentiment = WellKnown.Sentiments.Angry,
            SentimentScore = 0.1
        });

        signals.Should().NotBeNull();
        signals!.Intent.Should().BeNull("kural tabanlı intent tespiti devreye girmeli");
        signals.SentimentLabel.Should().Be(WellKnown.Sentiments.Angry);
    }

    [Fact]
    public void From_FullResult_CarriesBoth()
    {
        var signals = TurnSignals.From(new ReasoningResult
        {
            Intent = WellKnown.Intents.Complaint,
            Sentiment = WellKnown.Sentiments.Negative,
            SentimentScore = 0.2
        });

        signals.Should().BeEquivalentTo(
            new TurnSignals(WellKnown.Intents.Complaint, WellKnown.Sentiments.Negative, 0.2));
    }
}

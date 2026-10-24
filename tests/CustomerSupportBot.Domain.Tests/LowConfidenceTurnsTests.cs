// Ardışık düşük güvenli tur sayacı: SessionStateExtractor (tek yazar) tutar; TurnSignals güveni taşır.

using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;

namespace CustomerSupportBot.Domain.Tests;

public class LowConfidenceTurnsTests
{
    private static TurnSignals Signals(double confidence) => new(WellKnown.Intents.OrderInquiry, null, null, confidence);

    [Fact]
    public void ALowConfidenceReasoning_CarriesItsScore()
    {
        var s = TurnSignals.From(new ReasoningResult { Intent = WellKnown.Intents.OrderInquiry, ConfidenceScore = 0.3 });

        s!.ConfidenceScore.Should().Be(0.3);
    }

    [Fact]
    public void ALowConfidenceReasoning_WithNoIntent_IsStillASignal()
    {
        var s = TurnSignals.From(new ReasoningResult { Intent = WellKnown.Intents.Unknown, ConfidenceScore = 0.2 });

        s.Should().NotBeNull();
        s!.ConfidenceScore.Should().Be(0.2);
    }

    [Fact]
    public void AFallbackReasoning_IsNotEvidenceOfLowConfidence()
    {
        // Zaman aşımı/hata yer tutucusu (0.3) botun isteği anlayamadığı anlamına gelmez.
        var s = TurnSignals.From(new ReasoningResult
        {
            Intent = WellKnown.Intents.OrderInquiry, ConfidenceScore = 0.3, IsFallback = true
        });

        s!.ConfidenceScore.Should().BeNull();
    }

    [Fact]
    public void LowConfidenceTurns_CountConsecutively_AndResetOnAConfidentTurn()
    {
        var state = new SessionState();

        SessionStateExtractor.ExtractAndApply(state, "a", "b", llm: Signals(0.3));
        SessionStateExtractor.ExtractAndApply(state, "a", "b", llm: Signals(0.4));
        state.LowConfidenceTurns.Should().Be(2);

        SessionStateExtractor.ExtractAndApply(state, "a", "b", llm: Signals(0.9));
        state.LowConfidenceTurns.Should().Be(0);
    }

    [Fact]
    public void ATurnWithoutAConfidenceSignal_LeavesTheCounterAlone()
    {
        var state = new SessionState { LowConfidenceTurns = 1 };

        SessionStateExtractor.ExtractAndApply(state, "a", "b", llm: null);

        state.LowConfidenceTurns.Should().Be(1);
    }

    [Fact]
    public void TheThreshold_IsExclusive()
    {
        var state = new SessionState();

        SessionStateExtractor.ExtractAndApply(state, "a", "b", llm: Signals(WellKnown.ConfidenceThresholds.Low));

        state.LowConfidenceTurns.Should().Be(0);
    }
}

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

    // ── Netleştirme döngüsü: canlı veride ConfidenceScore hiç 0,6'nın altına inmedi (belirsiz mesajlarda bile 0,6–0,86),
    //    bu yüzden asıl sinyal "niyet genel kaldı ve plan yalnızca netleştirme sorusu" ───────────────────────────────

    private static ReasoningResult Clarifying(string intent = WellKnown.Intents.General, double confidence = 0.8, params string?[] actions) => new()
    {
        Intent = intent, ConfidenceScore = confidence,
        Steps = (actions.Length == 0 ? ["clarify"] : actions).Select((a, i) => new ReasoningStep { Order = i + 1, Action = a, Description = "x" }).ToList()
    };

    [Fact]
    public void AGeneralIntentWithOnlyClarifySteps_IsAnUnclearTurn_EvenWithHighStatedConfidence() =>
        TurnSignals.From(Clarifying())!.Clarifying.Should().BeTrue();

    [Theory]
    [InlineData(WellKnown.Intents.OrderCreation)]       // sipariş akışında ürün/adet sormak normaldir
    [InlineData(WellKnown.Intents.OrderCancellation)]
    [InlineData(WellKnown.Intents.Complaint)]
    public void ClarifyingWithinARecognisedIntent_IsNormal_NotUnclear(string intent) =>
        TurnSignals.From(Clarifying(intent))!.Clarifying.Should().BeFalse();

    [Fact]
    public void AGeneralTurnThatAlsoRoutesOrCallsATool_IsNotUnclear() =>
        TurnSignals.From(Clarifying(actions: ["extract", "clarify", "route"]))!.Clarifying.Should().BeFalse();

    [Fact]
    public void AGeneralTurnWithoutSteps_IsNotCalledUnclear() =>
        TurnSignals.From(new ReasoningResult { Intent = WellKnown.Intents.General, ConfidenceScore = 0.86, Steps = [] })!.Clarifying.Should().BeFalse();

    [Fact]
    public void ClarifyingTurns_CountTowardsTheStreak_AndAClearTurnResetsIt()
    {
        var state = new SessionState();
        var clarifying = TurnSignals.From(Clarifying())!;

        SessionStateExtractor.ExtractAndApply(state, "a", "b", llm: clarifying);
        SessionStateExtractor.ExtractAndApply(state, "a", "b", llm: clarifying);
        state.LowConfidenceTurns.Should().Be(2);

        SessionStateExtractor.ExtractAndApply(state, "a", "b",
            llm: TurnSignals.From(new ReasoningResult { Intent = WellKnown.Intents.OrderInquiry, ConfidenceScore = 0.86 }));
        state.LowConfidenceTurns.Should().Be(0);
    }

    [Fact]
    public void AFallbackReasoning_IsNeverCalledClarifying()
    {
        var r = Clarifying();
        r.IsFallback = true;

        TurnSignals.From(r)!.Clarifying.Should().BeFalse();
    }
}

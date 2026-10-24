// Düşük güvende devir kararı (saf): 1. düşük tur → temsilci önerisi, N. ardışık → otomatik eskalasyon.

using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Tests;

public class LowConfidenceHandoffPolicyTests
{
    private static readonly LowConfidenceHandoffOptions Opts = new();

    private static HandoffDecision Decide(int lowTurns, string? intent = WellKnown.Intents.OrderInquiry,
        ChatMode mode = ChatMode.Bot, bool openEscalation = false, LowConfidenceHandoffOptions? o = null) =>
        LowConfidenceHandoffPolicy.Decide(lowTurns, intent, mode, openEscalation, o ?? Opts);

    [Fact] public void NoLowTurns_DoesNothing() => Decide(0).Should().Be(HandoffDecision.None);
    [Fact] public void OneLowTurn_OffersAHuman() => Decide(1).Should().Be(HandoffDecision.Offer);
    [Fact] public void ConsecutiveLowTurns_Escalate() => Decide(2).Should().Be(HandoffDecision.Escalate);
    [Fact] public void MoreThanNeeded_StillEscalates() => Decide(5).Should().Be(HandoffDecision.Escalate);

    [Fact]
    public void WhenAHumanIsAlreadyInvolved_NothingHappens()
    {
        Decide(3, mode: ChatMode.Human).Should().Be(HandoffDecision.None);
        Decide(3, openEscalation: true).Should().Be(HandoffDecision.None, "aynı oturumda ikinci eskalasyon açılmaz");
    }

    [Fact]
    public void ExplicitHandoffRequests_AreLeftToTheirOwnPath() =>
        Decide(3, WellKnown.Intents.HumanHandoffRequest).Should().Be(HandoffDecision.None);

    [Fact]
    public void WhenDisabled_NothingHappens() =>
        Decide(5, o: new LowConfidenceHandoffOptions { Enabled = false }).Should().Be(HandoffDecision.None);

    [Fact]
    public void TheEscalationThreshold_IsConfigurable()
    {
        var o = new LowConfidenceHandoffOptions { ConsecutiveTurnsForEscalation = 3 };

        Decide(2, o: o).Should().Be(HandoffDecision.Offer);
        Decide(3, o: o).Should().Be(HandoffDecision.Escalate);
    }
}

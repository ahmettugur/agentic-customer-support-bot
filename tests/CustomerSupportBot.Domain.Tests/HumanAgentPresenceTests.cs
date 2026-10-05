// Temsilcinin geçerli durumu: saklanan durum + kalp atışı zaman aşımı.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Domain.Tests;

public class HumanAgentPresenceTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    [Theory]
    [InlineData(AgentPresence.Online)]
    [InlineData(AgentPresence.Away)]
    public void FreshHeartbeat_KeepsTheChosenPresence(AgentPresence chosen)
    {
        var agent = new HumanAgent { Presence = chosen, LastSeenAt = Now.AddSeconds(-30) };

        agent.EffectivePresence(Now, Timeout).Should().Be(chosen);
    }

    [Fact]
    public void StaleHeartbeat_IsOffline()
    {
        var agent = new HumanAgent { Presence = AgentPresence.Online, LastSeenAt = Now.AddSeconds(-91) };

        agent.EffectivePresence(Now, Timeout).Should().Be(AgentPresence.Offline);
    }

    [Fact]
    public void NeverSeen_IsOffline()
    {
        new HumanAgent { Presence = AgentPresence.Online }.EffectivePresence(Now, Timeout)
            .Should().Be(AgentPresence.Offline);
    }

    [Fact]
    public void ChosenOffline_StaysOffline_EvenWithFreshHeartbeat()
    {
        var agent = new HumanAgent { Presence = AgentPresence.Offline, LastSeenAt = Now };

        agent.EffectivePresence(Now, Timeout).Should().Be(AgentPresence.Offline);
    }

    [Fact]
    public void NewAgent_DefaultsToOffline()
    {
        new HumanAgent().Presence.Should().Be(AgentPresence.Offline);
    }
}

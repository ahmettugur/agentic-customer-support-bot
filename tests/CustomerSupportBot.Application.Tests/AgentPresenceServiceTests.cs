// Temsilci durumu: panel açılışı, seçim, kalp atışı, yönetici listesi.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Services.Escalation;
using CustomerSupportBot.Application.Services.Routing;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class AgentPresenceServiceTests
{
    private sealed record Harness(AgentPresenceService Service, InMemoryHumanAgentRegistry Registry, FakeTimeProvider Clock);

    private static Harness Build(params HumanAgent[] seeds)
    {
        var options = Options.Create(new RoutingOptions { SeedAgents = seeds.ToList(), PresenceTimeoutSeconds = 90 });
        var registry = new InMemoryHumanAgentRegistry(options);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
        return new Harness(new AgentPresenceService(registry, options, clock), registry, clock);
    }

    private static HumanAgent Agent(string id, bool active = true) =>
        new() { Id = id, DisplayName = id.ToUpperInvariant(), IsActive = active, MaxConcurrentLoad = 3 };

    [Fact]
    public void Connect_OfflineAgent_GoesOnline()
    {
        var h = Build(Agent("a1"));

        var info = h.Service.Connect("a1");

        info!.Presence.Should().Be(AgentPresence.Online);
        info.ChosenPresence.Should().Be(AgentPresence.Online);
        info.Since.Should().Be(h.Clock.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public void Connect_KeepsAway_SoRefreshingThePanelDoesNotEndABreak()
    {
        var h = Build(Agent("a1"));
        h.Service.Set("a1", AgentPresence.Away);
        h.Clock.Advance(TimeSpan.FromMinutes(10));   // panel kapalıydı, zaman aşımı geçti

        var info = h.Service.Connect("a1");

        info!.Presence.Should().Be(AgentPresence.Away);
    }

    [Fact]
    public void HeartbeatTimeout_MakesTheAgentOffline_AndAHeartbeatRestoresTheChoice()
    {
        var h = Build(Agent("a1"));
        h.Service.Connect("a1");

        h.Clock.Advance(TimeSpan.FromSeconds(91));
        h.Service.Get("a1")!.Presence.Should().Be(AgentPresence.Offline);
        h.Service.Get("a1")!.ChosenPresence.Should().Be(AgentPresence.Online);

        h.Service.Heartbeat("a1")!.Presence.Should().Be(AgentPresence.Online);
    }

    [Fact]
    public void Heartbeat_DoesNotOverrideAnExplicitOfflineChoice()
    {
        var h = Build(Agent("a1"));
        h.Service.Connect("a1");
        h.Service.Set("a1", AgentPresence.Offline);

        h.Service.Heartbeat("a1")!.Presence.Should().Be(AgentPresence.Offline);
    }

    [Fact]
    public void Since_ChangesOnlyWhenThePresenceChanges()
    {
        var h = Build(Agent("a1"));
        var since = h.Service.Connect("a1")!.Since;
        h.Clock.Advance(TimeSpan.FromSeconds(30));

        h.Service.Heartbeat("a1")!.Since.Should().Be(since);
        h.Service.Set("a1", AgentPresence.Online)!.Since.Should().Be(since, "aynı durumu yeniden seçmek süreyi sıfırlamaz");
        h.Service.Set("a1", AgentPresence.Away)!.Since.Should().Be(h.Clock.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public void UnknownAgent_ReturnsNull()
    {
        var h = Build();

        h.Service.Get("yok").Should().BeNull();
        h.Service.Connect("yok").Should().BeNull();
        h.Service.Set("yok", AgentPresence.Online).Should().BeNull();
        h.Service.Heartbeat("yok").Should().BeNull();
    }

    [Fact]
    public void GetAll_ListsActiveAgents_OnlineFirst_ThenAway_ThenOffline()
    {
        var h = Build(Agent("cem"), Agent("ali"), Agent("bora"), Agent("pasif", active: false));
        h.Service.Connect("bora");
        h.Service.Connect("cem");
        h.Service.Set("cem", AgentPresence.Away);

        h.Service.GetAll().Select(a => (a.AgentId, a.Presence)).Should().Equal(
            ("bora", AgentPresence.Online),
            ("cem", AgentPresence.Away),
            ("ali", AgentPresence.Offline));
    }

    [Fact]
    public void Info_CarriesTheLoad()
    {
        var h = Build(Agent("a1"));
        h.Registry.IncrementLoad("a1");

        var info = h.Service.Get("a1")!;

        info.CurrentLoad.Should().Be(1);
        info.MaxConcurrentLoad.Should().Be(3);
        info.DisplayName.Should().Be("A1");
    }
}

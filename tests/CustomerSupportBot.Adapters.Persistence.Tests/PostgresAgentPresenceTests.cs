// Temsilci durumu — gerçek Postgres + pod'lar arası yayın. Durum yazımı ve yayını yalnızca durum
// alanlarını taşımalı: her 30 sn'lik kalp atışı, yayınlayan pod'un (eski olabilecek) yük sayacını
// diğer pod'lara ya da veritabanına yazmamalı.

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Hitl;
using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresAgentPresenceTests(PostgresCatalogFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTime T0 = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    private async Task<string> InsertAgentAsync()
    {
        var id = $"p{Guid.NewGuid():N}"[..24];
        await using var ctx = fixture.DbFactory.CreateDbContext();
        ctx.HumanAgents.Add(new HumanAgentEntity
        {
            Id = id, DisplayName = "Durum Testi", IsActive = true, MaxConcurrentLoad = 5, CreatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync(Ct);
        return id;
    }

    private PostgresHumanAgentRegistry Pod(InMemoryMessageBusHub hub) =>
        new(fixture.DbFactory, hub.CreateNode(), NullLogger<PostgresHumanAgentRegistry>.Instance);

    private PostgresHumanAgentRegistry IsolatedPod() =>
        new(fixture.DbFactory, new NoopMessageBus(), NullLogger<PostgresHumanAgentRegistry>.Instance);

    [Fact]
    public async Task Presence_IsPersisted()
    {
        var id = await InsertAgentAsync();
        var writer = IsolatedPod();

        writer.SetPresence(id, AgentPresence.Away, T0).Should().BeTrue();
        writer.TouchPresence(id, T0.AddSeconds(30)).Should().BeTrue();

        var restarted = IsolatedPod().Get(id)!;
        restarted.Presence.Should().Be(AgentPresence.Away);
        restarted.PresenceChangedAt.Should().Be(T0);
        restarted.LastSeenAt.Should().Be(T0.AddSeconds(30));
    }

    [Fact]
    public async Task Presence_ReachesOtherPods()
    {
        var id = await InsertAgentAsync();
        var hub = new InMemoryMessageBusHub();
        var (a, b) = (Pod(hub), Pod(hub));
        b.Get(id).Should().NotBeNull();   // b önbelleğini doldurdu

        a.SetPresence(id, AgentPresence.Online, T0);
        b.Get(id)!.Presence.Should().Be(AgentPresence.Online);

        a.TouchPresence(id, T0.AddSeconds(30));
        b.Get(id)!.LastSeenAt.Should().Be(T0.AddSeconds(30));
    }

    [Fact]
    public async Task Heartbeat_DoesNotSpreadAStaleLoad_ToTheDbOrOtherPods()
    {
        var id = await InsertAgentAsync();
        var hub = new InMemoryMessageBusHub();
        var (a, b) = (Pod(hub), Pod(hub));
        a.Get(id).Should().NotBeNull();
        b.IncrementLoad(id);
        a.Get(id)!.CurrentLoad = 0;   // a yük mesajını kaçırdı (Redis en-fazla-bir-kez)

        a.SetPresence(id, AgentPresence.Online, T0);
        a.TouchPresence(id, T0.AddSeconds(30));

        b.Get(id)!.CurrentLoad.Should().Be(1, "durum yayını yük taşımaz");
        await using var ctx = fixture.DbFactory.CreateDbContext();
        (await ctx.HumanAgents.SingleAsync(e => e.Id == id, Ct)).CurrentLoad.Should().Be(1, "durum yazımı yük sütununa dokunmaz");
    }

    [Fact]
    public async Task FullRecordBroadcast_KeepsTheNewerPresence()
    {
        var id = await InsertAgentAsync();
        var hub = new InMemoryMessageBusHub();
        var (a, b) = (Pod(hub), Pod(hub));
        a.Get(id).Should().NotBeNull();
        b.SetPresence(id, AgentPresence.Online, T0.AddMinutes(5));
        var stale = a.Get(id)!;   // a durum mesajını kaçırdı
        stale.Presence = AgentPresence.Offline;
        stale.PresenceChangedAt = T0;
        stale.LastSeenAt = T0;

        a.IncrementLoad(id);   // a tam kaydı yayınlar

        var seenByB = b.Get(id)!;
        seenByB.CurrentLoad.Should().Be(1);
        seenByB.Presence.Should().Be(AgentPresence.Online);
        seenByB.LastSeenAt.Should().Be(T0.AddMinutes(5));
    }

    [Fact]
    public async Task AdminUpdate_DoesNotOverwritePresenceInTheDb()
    {
        var id = await InsertAgentAsync();
        var hub = new InMemoryMessageBusHub();
        var (a, b) = (Pod(hub), IsolatedPod());
        b.Get(id).Should().NotBeNull();   // b'nin önbelleğinde durum Offline kalacak
        a.SetPresence(id, AgentPresence.Online, T0);

        b.Update(id, new HumanAgentInput { DisplayName = "Yeni Ad" });

        var reloaded = IsolatedPod().Get(id)!;
        reloaded.DisplayName.Should().Be("Yeni Ad");
        reloaded.Presence.Should().Be(AgentPresence.Online);
    }
}

// Kişisel veri silme (KVKK) — gerçek Postgres. Her depo hem kalıcı kaydı hem de TÜM pod'ların
// önbelleğini temizlemeli: iki "pod" aynı mesaj yoluna bağlanır, biri siler, diğerinin önbelleğinde
// veri kalmadığı ölçülür.

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Chat;
using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresSessionDataErasureTests(PostgresCatalogFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string NewSessionId() => $"erase-{Guid.NewGuid():N}";

    private async Task InsertSessionAsync(string sessionId, DateTime lastActivityUtc)
    {
        await using var ctx = fixture.DbFactory.CreateDbContext();
        ctx.Sessions.Add(new SessionEntity
        {
            SessionId = sessionId, CreatedAt = lastActivityUtc, LastActivity = lastActivityUtc, StateJson = "{}"
        });
        await ctx.SaveChangesAsync(Ct);
    }

    // ─── Oturum yöneticisi ve fotoğraflar ────────────────────────────────────

    [Fact]
    public async Task GetInactiveSessionIds_ReturnsOldestFirst_AndRespectsTheLimit()
    {
        var cutoff = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);   // diğer testlerin oturumları bundan yeni
        var oldest = NewSessionId();
        var older = NewSessionId();
        var recent = NewSessionId();
        await InsertSessionAsync(oldest, cutoff.AddDays(-30));
        await InsertSessionAsync(older, cutoff.AddDays(-10));
        await InsertSessionAsync(recent, cutoff.AddDays(1));
        var manager = new PostgresSessionManager(fixture.DbFactory,
            new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 })),
            new NoopMessageBus(), NullLogger<PostgresSessionManager>.Instance);

        var all = await manager.GetInactiveSessionIdsAsync(cutoff, 100, Ct);
        var one = await manager.GetInactiveSessionIdsAsync(cutoff, 1, Ct);

        all.Should().ContainInOrder(oldest, older).And.NotContain(recent);
        one.Should().Equal(oldest);
    }

    [Fact]
    public async Task Attachments_DeleteCreatedBefore_AndEraseSessions()
    {
        var keep = NewSessionId();
        var erase = NewSessionId();
        await InsertSessionAsync(keep, DateTime.UtcNow);
        await InsertSessionAsync(erase, DateTime.UtcNow);
        var store = new PostgresAttachmentStore(fixture.DbFactory);
        ChatAttachment Photo(string sid, DateTime created) => new()
        {
            SessionId = sid, CustomerId = "1001", ContentType = "image/jpeg", Data = [1], CreatedAt = created
        };
        var veryOld = Photo(keep, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var fresh = Photo(keep, DateTime.UtcNow);
        var inErasedSession = Photo(erase, DateTime.UtcNow);
        foreach (var p in new[] { veryOld, fresh, inErasedSession }) await store.SaveAsync(p, Ct);

        (await store.DeleteCreatedBeforeAsync(new DateTime(2000, 6, 1, 0, 0, 0, DateTimeKind.Utc), Ct)).Should().BeGreaterThanOrEqualTo(1);
        (await store.EraseSessionsAsync([erase], Ct)).Should().Be(1);

        (await store.GetAsync(veryOld.Id, Ct)).Should().BeNull();
        (await store.GetAsync(inErasedSession.Id, Ct)).Should().BeNull();
        (await store.GetAsync(fresh.Id, Ct)).Should().NotBeNull();
    }

    // ─── Önbellekli depolar: silen pod + diğer pod ───────────────────────────

    [Fact]
    public async Task Ratings_AreErasedFromDbAndEveryPodsCache()
    {
        var hub = new InMemoryMessageBusHub();
        PostgresRatingStore Pod() => new(fixture.DbFactory, hub.CreateNode(), NullLogger<PostgresRatingStore>.Instance);
        var (a, b) = (Pod(), Pod());
        var sid = NewSessionId();
        var other = NewSessionId();
        await a.SubmitAsync(sid, 5, "iyiydi, telefonum 0555");
        await a.SubmitAsync(other, 4, null);
        await b.WarmUpAsync(Ct);
        b.GetBySession(sid).Should().NotBeNull();

        var n = await a.EraseSessionsAsync([sid], Ct);

        n.Should().Be(1);
        a.GetBySession(sid).Should().BeNull();
        b.GetBySession(sid).Should().BeNull("diğer pod'un önbelleği de temizlenmeli");
        b.GetBySession(other).Should().NotBeNull();
        await using var ctx = fixture.DbFactory.CreateDbContext();
        (await ctx.Ratings.CountAsync(r => r.SessionId == sid, Ct)).Should().Be(0);
    }

    [Fact]
    public async Task ChatModes_AreErasedFromDbAndEveryPodsCache()
    {
        var hub = new InMemoryMessageBusHub();
        var locks = new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 }));
        PostgresChatModeRegistry Pod() => new(fixture.DbFactory, hub.CreateNode(), locks, NullLogger<PostgresChatModeRegistry>.Instance);
        var (a, b) = (Pod(), Pod());
        var sid = NewSessionId();
        await a.TakeOverAsync(sid, "admin");
        await b.WarmUpAsync(Ct);
        b.GetState(sid).Should().NotBeNull();

        await a.EraseSessionsAsync([sid], Ct);

        a.GetState(sid).Should().BeNull();
        b.GetState(sid).Should().BeNull();
        await using var ctx = fixture.DbFactory.CreateDbContext();
        (await ctx.ChatSessionModes.CountAsync(m => m.SessionId == sid, Ct)).Should().Be(0);
    }

    [Fact]
    public async Task BridgeMessages_AreErasedFromDbAndEveryPodsCache()
    {
        var hub = new InMemoryMessageBusHub();
        PostgresChatBridge Pod() => new(fixture.DbFactory, hub.CreateNode(), NullLogger<PostgresChatBridge>.Instance);
        var (a, b) = (Pod(), Pod());
        var sid = NewSessionId();
        await a.RecordBotExchangeAsync(sid, "adresim Kadıköy", "teşekkürler");
        (await b.GetHistoryAsync(sid)).Should().NotBeEmpty();

        await a.EraseSessionsAsync([sid], Ct);

        (await a.GetHistoryAsync(sid)).Should().BeEmpty();
        (await b.GetHistoryAsync(sid)).Should().BeEmpty("önbellek temizlenip DB'den yeniden okunduğunda kayıt yok");
    }

    [Fact]
    public async Task ReasoningTraces_AreErasedFromDbAndEveryPodsCache()
    {
        var hub = new InMemoryMessageBusHub();
        PostgresReasoningTraceStore Pod() => new(fixture.DbFactory, hub.CreateNode(), NullLogger<PostgresReasoningTraceStore>.Instance);
        var (a, b) = (Pod(), Pod());
        var sid = NewSessionId();
        var other = NewSessionId();
        await a.StartTraceAsync(sid, "siparişim nerede");
        await a.StartTraceAsync(other, "merhaba");
        await b.WarmUpAsync(Ct);
        b.GetBySession(sid).Should().NotBeEmpty();

        var n = await a.EraseSessionsAsync([sid], Ct);

        n.Should().Be(1);
        a.GetBySession(sid).Should().BeEmpty();
        b.GetBySession(sid).Should().BeEmpty();
        b.GetBySession(other).Should().NotBeEmpty();
        await using var ctx = fixture.DbFactory.CreateDbContext();
        (await ctx.ReasoningTraces.CountAsync(t => t.SessionId == sid, Ct)).Should().Be(0);
    }

    /// <summary>
    /// Kapanmış eskalasyon silinir. Açık olan silinmez — temsilci kuyruğu ve yük sayaçları bozulmasın —
    /// ama içindeki müşteri metni temizlenir (DB ve tüm önbellekler).
    /// </summary>
    [Fact]
    public async Task Escalations_ClosedAreDeleted_OpenAreScrubbed_OnEveryPod()
    {
        var hub = new InMemoryMessageBusHub();
        PostgresEscalationSink Pod() => new(fixture.DbFactory, hub.CreateNode(), NullLogger<PostgresEscalationSink>.Instance);
        var (a, b) = (Pod(), Pod());
        var sid = NewSessionId();
        var closed = await a.CreateAsync(new EscalationRequest { SessionId = sid, AgentName = "A1", UserQuery = "iade istiyorum, adresim X", Reason = "test-closed" });
        await a.DecideAsync(closed.Id, WellKnown.EscalationActions.Resolve, "agent-1", "iade yapıldı");
        var open = await a.CreateAsync(new EscalationRequest
        {
            SessionId = sid, AgentName = "A2", UserQuery = "kargom gelmedi, telefonum 0555", Reason = "test-open", ResponseSummary = "özet"
        });
        await b.WarmUpAsync(Ct);

        var n = await a.EraseSessionsAsync([sid], Ct);

        n.Should().Be(2);
        a.Get(closed.Id).Should().BeNull();
        b.Get(closed.Id).Should().BeNull();
        foreach (var pod in new[] { a, b })
        {
            var kept = pod.Get(open.Id);
            kept.Should().NotBeNull("açık eskalasyon kuyrukta kalır");
            kept!.UserQuery.Should().NotContain("0555");
            kept.ResponseSummary.Should().BeNull();
        }
        await using var ctx = fixture.DbFactory.CreateDbContext();
        (await ctx.Escalations.AnyAsync(e => e.Id == closed.Id, Ct)).Should().BeFalse();
        (await ctx.Escalations.SingleAsync(e => e.Id == open.Id, Ct)).UserQuery.Should().NotContain("0555");
    }
}

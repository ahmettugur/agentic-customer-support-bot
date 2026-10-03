// PostgresSessionManager — veri kaybı ve referans tutarlılığı regresyonları (gerçek Postgres).
//
// 1. Hydrate HATAYLA bittiğinde GetOrCreateAsync "oturum yok" sanıp boş bir oturum
//    oluşturuyor ve UPSERT ile DB'deki gerçek state'i (müşteri sahipliği dahil) eziyordu.
// 2. Reload / hydrate / pub-sub her seferinde _sessions[id]'yi YENİ bir nesneyle
//    değiştiriyordu: elinde eski referansı tutan çağıran (kilit anahtarı + durum taşıyıcısı)
//    bayat state'le çalışıyordu. Sırasız gelen eski bir snapshot da yeni durumu eziyordu.

using System.Text.Json;
using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Chat;
using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Outbound.Locking;
using CustomerSupportBot.Application.Ports.Outbound.Messaging;
using CustomerSupportBot.Domain.Exceptions;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresSessionManagerConsistencyTests
{
    private readonly PostgresCatalogFixture _fixture;
    private readonly IAppDistributedLock _lock =
        new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 }));

    public PostgresSessionManagerConsistencyTests(PostgresCatalogFixture fixture) => _fixture = fixture;

    private PostgresSessionManager NewManager(
        IDbContextFactory<CustomerSupportDbContext> dbFactory, IMessageBusPort? bus = null)
        => new(dbFactory, _lock, bus ?? new NoopMessageBus(), NullLogger<PostgresSessionManager>.Instance);

    private async Task InsertSessionRowAsync(string sessionId, string customerId)
    {
        await using var ctx = _fixture.DbFactory.CreateDbContext();
        ctx.Sessions.Add(new SessionEntity
        {
            SessionId = sessionId,
            CreatedAt = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow,
            StateJson = $$"""{"CustomerId":"{{customerId}}","AuthenticatedCustomerId":"{{customerId}}"}"""
        });
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<string> ReadStateJsonAsync(string sessionId)
    {
        await using var ctx = _fixture.DbFactory.CreateDbContext();
        return (await ctx.Sessions.AsNoTracking()
            .SingleAsync(s => s.SessionId == sessionId, TestContext.Current.CancellationToken)).StateJson;
    }

    [Fact]
    public async Task GetOrCreate_WhenHydrationFails_DoesNotOverwriteExistingState()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = $"h2-{Guid.NewGuid():N}";
        await InsertSessionRowAsync(sessionId, "1027");

        // Yalnızca hydrate okuması başarısız olur; sonraki UPSERT başarılı olurdu — eski kod
        // tam bu boşlukta boş state'i DB'ye yazıyordu.
        var mgr = NewManager(new FlakyDbContextFactory(_fixture.DbFactory, failuresRemaining: 1));

        var act = () => mgr.GetOrCreateAsync(sessionId, ct);
        await act.Should().ThrowAsync<DomainException>(
            "oturumun DB'de olup olmadığı bilinmeden yeni bir oturum oluşturulmamalı");

        (await ReadStateJsonAsync(sessionId)).Should().Contain("1027",
            "başarısız hydrate DB'deki müşteri sahipliğini silmemeli");

        var session = await mgr.GetOrCreateAsync(sessionId, ct);
        session.State.AuthenticatedCustomerId.Should().Be("1027",
            "sonraki deneme DB'yi yeniden okumalı ve gerçek state'i görmeli");
    }

    [Fact]
    public async Task GetOrCreate_WhenSessionGenuinelyMissing_StillCreates()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = $"h2-new-{Guid.NewGuid():N}";

        var session = await NewManager(_fixture.DbFactory).GetOrCreateAsync(sessionId, ct);

        session.SessionId.Should().Be(sessionId);
        (await ReadStateJsonAsync(sessionId)).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Reload_KeepsTheSameObjectReference()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = $"h3-reload-{Guid.NewGuid():N}";
        await InsertSessionRowAsync(sessionId, "1008");

        var mgr = NewManager(_fixture.DbFactory);
        var held = await mgr.GetOrCreateAsync(sessionId, ct);

        var reloaded = await mgr.ReloadAsync(sessionId, ct);

        reloaded.Should().BeSameAs(held,
            "çağıranlar bu nesneyi kilit anahtarı olarak kullanıyor; reload onu değiştirmemeli");
        reloaded.State.AuthenticatedCustomerId.Should().Be("1008");
    }

    [Fact]
    public async Task RemoteUpdate_IsAppliedToTheHeldReference()
    {
        var ct = TestContext.Current.CancellationToken;
        var hub = new InMemoryMessageBusHub();
        var sessionId = $"h3-remote-{Guid.NewGuid():N}";

        var writer = NewManager(_fixture.DbFactory, hub.CreateNode());
        var reader = NewManager(_fixture.DbFactory, hub.CreateNode());

        var writerSession = await writer.GetOrCreateAsync(sessionId, ct);
        var held = await reader.GetAsync(sessionId, ct);
        held.Should().NotBeNull();

        writerSession.State.CustomerId = "1042";
        await writer.UpdateAsync(writerSession, ct);

        held!.State.CustomerId.Should().Be("1042",
            "başka pod'un güncellemesi okuyucunun elindeki nesneye yansımalı");
        (await reader.GetAsync(sessionId, ct)).Should().BeSameAs(held);
    }

    [Fact]
    public async Task RemoteUpdate_WithOlderRevision_IsIgnored()
    {
        var ct = TestContext.Current.CancellationToken;
        var hub = new InMemoryMessageBusHub();
        var sessionId = $"h3-stale-{Guid.NewGuid():N}";

        var writer = NewManager(_fixture.DbFactory, hub.CreateNode());
        var reader = NewManager(_fixture.DbFactory, hub.CreateNode());

        var s = await writer.GetOrCreateAsync(sessionId, ct);
        s.State.CustomerId = "first";
        await writer.UpdateAsync(s, ct);
        s.State.CustomerId = "second";
        await writer.UpdateAsync(s, ct);

        var readerSession = await reader.GetAsync(sessionId, ct);
        readerSession!.State.Revision.Should().Be(2);
        readerSession.State.CustomerId.Should().Be("second");

        // Sırasız teslim: revizyon 1'e ait eski snapshot geç geliyor.
        var stale = JsonSerializer.Serialize(new
        {
            nodeId = "stale-pod",
            sessionId,
            createdAt = DateTime.Now,
            lastActivity = DateTime.Now,
            state = new SessionState { CustomerId = "first", Revision = 1 }
        });
        hub.CreateNode().Publish("csbot:session:updated", stale);

        readerSession.State.CustomerId.Should().Be("second",
            "daha düşük revizyonlu snapshot daha yeni durumu ezmemeli");
        readerSession.State.Revision.Should().Be(2);
    }

    [Fact]
    public async Task Update_PersistsIncrementingRevision()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = $"h3-rev-{Guid.NewGuid():N}";

        var mgr = NewManager(_fixture.DbFactory);
        var s = await mgr.GetOrCreateAsync(sessionId, ct);
        await mgr.UpdateAsync(s, ct);
        await mgr.UpdateAsync(s, ct);

        var fresh = await NewManager(_fixture.DbFactory).GetAsync(sessionId, ct);
        fresh!.State.Revision.Should().Be(2);
    }
}

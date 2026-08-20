// Tests/Services/PostgresSessionManagerHydrationTests.cs
//
// Postgres adaptörlerinde hydration+cache deseni 11 sınıfta tekrarlanıyor ama hiçbiri
// gerçek bir Postgres'e karşı test edilmiyordu — tüm adaptör testleri InMemory varyantları
// hedefliyordu. Bu, gerçek bir üretim hatasının (aşağıda) uzun süre fark edilmemesine
// neden oldu: EnsureSessionHydrated, DB çağrısı BAŞARISIZ olsa bile "hydrate edildi" flag'ini
// kalıcı olarak set ediyordu (flag try'dan ÖNCE, TryAdd ile konuyor ve hata durumunda geri
// alınmıyordu) — geçici bir DB hatası (timeout, deadlock) o session'ı process ömrü boyunca
// "boş" olarak kilitliyordu. Bu test gerçek Testcontainers Postgres'e karşı, ilk hydrate
// denemesini yapay olarak başarısız kılıp ikinci denemenin gerçekten DB'ye ulaştığını
// (yani flag'in geri alındığını) kanıtlıyor.

using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Chat;
using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Adapters.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using CustomerSupportBot.Tests.Shared;
using CustomerSupportBot.Application.Ports.Outbound.Locking;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresSessionManagerHydrationTests
{
    private readonly PostgresCatalogFixture _fixture;
    private readonly IAppDistributedLock _lock =
        new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 }));

    public PostgresSessionManagerHydrationTests(PostgresCatalogFixture fixture) => _fixture = fixture;

    private PostgresSessionManager NewManager(IDbContextFactory<CustomerSupportDbContext> dbFactory)
        => new(dbFactory, _lock, new NoopMessageBus(), NullLogger<PostgresSessionManager>.Instance);

    private async Task InsertSessionRowAsync(string sessionId, string customerId)
    {
        await using var ctx = _fixture.DbFactory.CreateDbContext();
        ctx.Sessions.Add(new SessionEntity
        {
            SessionId = sessionId,
            CreatedAt = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow,
            StateJson = $$"""{"CustomerId":"{{customerId}}"}"""
        });
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Get_ExistingSessionRow_HydratesFromDb()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = $"hydrate-{Guid.NewGuid():N}";
        await InsertSessionRowAsync(sessionId, "1027");

        var mgr = NewManager(_fixture.DbFactory);
        var session = await mgr.GetAsync(sessionId, ct);

        session.Should().NotBeNull();
        session!.State.CustomerId.Should().Be("1027");
    }

    [Fact]
    public async Task Get_TransientHydrationFailure_RetriesOnNextCall()
    {
        // Asıl regresyon: ilk çağrı DB hatasıyla başarısız olur. Flag doğru geri
        // alınmıyorsa ikinci çağrı DB'yi HİÇ denemez ve session hep null/boş kalır.
        var ct = TestContext.Current.CancellationToken;
        var sessionId = $"hydrate-retry-{Guid.NewGuid():N}";
        await InsertSessionRowAsync(sessionId, "1008");

        var flaky = new FlakyDbContextFactory(_fixture.DbFactory, failuresRemaining: 1);
        var mgr = NewManager(flaky);

        var firstAttempt = await mgr.GetAsync(sessionId, ct);
        firstAttempt.Should().BeNull(
            "ilk deneme DB hatasıyla başarısız olmalı — bu sırada session hiç cache'e girmemeli");

        var secondAttempt = await mgr.GetAsync(sessionId, ct);
        secondAttempt.Should().NotBeNull(
            "flag geri alınmadıysa ikinci deneme DB'yi hiç sorgulamaz ve session sonsuza dek null kalırdı");
        secondAttempt!.State.CustomerId.Should().Be("1008");
    }

    [Fact]
    public async Task AddExchange_PersistsHistory_VisibleAfterRehydration()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = $"exchange-{Guid.NewGuid():N}";
        var mgr1 = NewManager(_fixture.DbFactory);
        await mgr1.AddExchangeAsync(sessionId, "merhaba", "size nasıl yardımcı olabilirim", ct: ct);

        // Yeni bir manager instance'ı — cache boş, geçmişi DB'den hydrate etmek zorunda.
        var mgr2 = NewManager(_fixture.DbFactory);
        var history = await mgr2.GetHistoryAsync(sessionId, ct);

        history.Should().HaveCount(2);
        history[0].Text.Should().Be("merhaba");
    }

    // ─── Redis pub/sub cross-pod senkronizasyonu ───────────────────────────────
    // Bu adaptör 11 Postgres adaptöründen biriydi ve tek başına MessageBus'a bağlı
    // DEĞİLDİ — çoklu instance/pod dağıtımında pod A'nın state değişikliği pod B'ye
    // hiç ulaşmıyordu. Aşağıdaki testler "reader" pod'un DB'YE HİÇ ERİŞEMEDİĞİ (her
    // zaman hata veren bir factory ile) bir kurulumda veriye yalnızca pub/sub üzerinden
    // ulaştığını kanıtlıyor — DB'den kaçak bir hydrate ile yanlışlıkla geçme ihtimali yok.

    [Fact]
    public async Task Update_PublishesSessionState_VisibleOnOtherPodWithoutDbAccess()
    {
        var ct = TestContext.Current.CancellationToken;
        var hub = new InMemoryMessageBusHub();
        var sessionId = $"sync-{Guid.NewGuid():N}";

        var writer = new PostgresSessionManager(
            _fixture.DbFactory, _lock, hub.CreateNode(), NullLogger<PostgresSessionManager>.Instance);
        var neverReachesDb = new FlakyDbContextFactory(_fixture.DbFactory, failuresRemaining: int.MaxValue);
        var reader = new PostgresSessionManager(
            neverReachesDb, _lock, hub.CreateNode(), NullLogger<PostgresSessionManager>.Instance);

        var session = await writer.GetOrCreateAsync(sessionId, ct);
        session.State.CustomerId = "1027";
        await writer.UpdateAsync(session, ct);

        var seenByReader = await reader.GetAsync(sessionId, ct);

        seenByReader.Should().NotBeNull(
            "reader'ın DB'si her zaman hata veriyor — bu değer yalnızca Redis pub/sub üzerinden gelebilir");
        seenByReader!.State.CustomerId.Should().Be("1027");
    }

    [Fact]
    public async Task AddExchange_PublishesHistoryDelta_VisibleOnOtherPodWithoutFurtherDbAccess()
    {
        var ct = TestContext.Current.CancellationToken;
        var hub = new InMemoryMessageBusHub();
        var sessionId = $"sync-history-{Guid.NewGuid():N}";

        var writer = new PostgresSessionManager(
            _fixture.DbFactory, _lock, hub.CreateNode(), NullLogger<PostgresSessionManager>.Instance);
        await writer.GetOrCreateAsync(sessionId, ct); // DB'de session satırı oluşturulur

        var reader = new PostgresSessionManager(
            _fixture.DbFactory, _lock, hub.CreateNode(), NullLogger<PostgresSessionManager>.Instance);

        // Reader'ın bu session için ÖNCEDEN bir cache girdisi olması gerekiyor — delta
        // yalnızca zaten izlenen session'lara uygulanır (fleet genelinde hiç görülmemiş
        // session'lar için kısmi liste birikmesin diye). Bu TEK hydrate çağrısı DB'ye
        // gerçekten gider (session satırı var, mesaj yok → boş liste cache'lenir) ve
        // _hydratedSessions flag'i BU SESSION İÇİN KALICI OLARAK set edilir — sonraki hiçbir
        // çağrı bu session için DB'ye tekrar gitmez (bkz. EnsureSessionHydrated).
        (await reader.GetHistoryAsync(sessionId, ct)).Should().BeEmpty();

        await writer.AddExchangeAsync(sessionId, "merhaba", "size nasıl yardımcı olabilirim", ct: ct);

        // Reader zaten hydrate edilmiş olduğu için bu çağrı DB'ye BİR DAHA gitmez —
        // aşağıdaki iki mesaj yalnızca Redis delta'sından (OnRemoteHistoryChanged) gelebilir.
        var seenByReader = await reader.GetHistoryAsync(sessionId, ct);

        seenByReader.Should().HaveCount(2);
        seenByReader[0].Text.Should().Be("merhaba");
        seenByReader[1].Text.Should().Be("size nasıl yardımcı olabilirim");
    }

    // ─── Eşzamanlı ilk hydrate ─────────────────────────────────────────────────
    //
    // Hydrate "yapılıyor mu" bilgisi bir BAYRAKTI ve bayrak, DB okuması başlamadan ÖNCE
    // konuyordu. İkinci eşzamanlı çağrı bayrağı görüp hemen dönüyor, yani hydrate hâlâ
    // sürerken boş bir session'la devam ediyordu. Bunun bedeli kalıcıdır: o boş State
    // üzerinden yapılan bir yazma, DB'deki gerçek state'i — müşteri sahipliği dahil — {} ile
    // ezer. Aynı oturuma iki isteğin hemen ardışık gelmesi bunun için yeterlidir.

    /// <summary>
    /// İki eşzamanlı çağrı: birincisi hydrate'i başlatıp DB'de bekler, ikincisi bu sırada
    /// gelir. İkisi de DB'deki müşteri sahipliğini görmelidir.
    /// </summary>
    [Fact]
    public async Task ConcurrentFirstAccess_BothCallersSeeTheHydratedState()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = $"hydrate-race-{Guid.NewGuid():N}";
        await InsertSessionRowAsync(sessionId, "1027");

        var gated = new GatedDbContextFactory(_fixture.DbFactory);
        var mgr = NewManager(gated);

        // Birinci çağıran hydrate'e girer ve kapıda bekler.
        var first = Task.Run(() => mgr.GetAsync(sessionId, ct), ct);
        await gated.FirstCallEntered.WaitAsync(TimeSpan.FromSeconds(10), ct);

        // İkinci çağıran tam bu anda gelir — hydrate HENÜZ BİTMEDİ.
        var second = Task.Run(() => mgr.GetAsync(sessionId, ct), ct);

        gated.OpenGate();

        var firstResult = await first.WaitAsync(TimeSpan.FromSeconds(30), ct);
        var secondResult = await second.WaitAsync(TimeSpan.FromSeconds(30), ct);

        firstResult.Should().NotBeNull();
        firstResult!.State.CustomerId.Should().Be("1027");

        secondResult.Should().NotBeNull(
            "hydrate sürerken gelen çağrı, işin bitmesini beklemeli — boş bir session'la dönmemeli");
        secondResult!.State.CustomerId.Should().Be("1027",
            "boş State ile devam etmek, sonraki bir yazmada DB'deki müşteri sahipliğini silerdi");
    }

    /// <summary>
    /// Yarışın asıl zararı: hydrate beklenmediğinde boş State üzerinden yapılan yazma kalıcı
    /// sahipliği siler. Burada ikinci çağıranın gördüğü session DB'ye geri yazılıyor ve
    /// sahipliğin hayatta kalıp kalmadığına bakılıyor.
    /// </summary>
    [Fact]
    public async Task ConcurrentFirstAccess_DoesNotWipeOwnershipOnWriteBack()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = $"hydrate-wipe-{Guid.NewGuid():N}";
        await InsertSessionRowAsync(sessionId, "1008");

        var gated = new GatedDbContextFactory(_fixture.DbFactory);
        var mgr = NewManager(gated);

        var first = Task.Run(() => mgr.GetOrCreateAsync(sessionId, ct), ct);
        await gated.FirstCallEntered.WaitAsync(TimeSpan.FromSeconds(10), ct);
        var second = Task.Run(() => mgr.GetOrCreateAsync(sessionId, ct), ct);
        gated.OpenGate();

        await first.WaitAsync(TimeSpan.FromSeconds(30), ct);
        var secondSession = await second.WaitAsync(TimeSpan.FromSeconds(30), ct);

        // İkinci çağıranın elindeki session normal akışta olduğu gibi geri yazılır.
        await mgr.UpdateAsync(secondSession, ct);

        // Taze bir manager — yalnızca DB'de kalanı görür.
        var verifier = NewManager(_fixture.DbFactory);
        var persisted = await verifier.GetAsync(sessionId, ct);

        persisted.Should().NotBeNull();
        persisted!.State.CustomerId.Should().Be("1008",
            "müşteri sahipliği bir yarış yüzünden kalıcı olarak silinmemeli");
    }
}

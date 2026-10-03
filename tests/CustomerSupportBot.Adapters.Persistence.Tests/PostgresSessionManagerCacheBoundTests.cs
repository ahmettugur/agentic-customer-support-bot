// PostgresSessionManager cache'i SINIRLI mı, ve "yok" sonucu kalıcı mı?
//
// İki ayrı sorun, aynı kökten: cache'e giren hiçbir şey çıkmıyordu.
//
// 1. ISKALAMA KALICIYDI. Var olmayan bir oturum sorgulandığında (ör. /chat/events/{id}, ilk
//    mesajdan önce açılan olay akışı) hydrate "tamamlandı" olarak cache'leniyordu. Oturum
//    sonradan BAŞKA bir pod'da oluşturulduğunda bu pod onu bir daha DB'den okumuyordu:
//    GetAsync null dönüyor, GetOrCreateAsync ise boş state'li yeni bir nesne üretip DB'deki
//    gerçek state'i (müşteri sahipliği, özet, sayaçlar) {} ile eziyordu. Rastgele id'lerle
//    yapılan sorgular da süreç ömrü boyunca birikiyordu.
//
// 2. EVICTION YOKTU. Görülen her oturum ve tüm mesaj geçmişi süreç ömrü boyunca bellekte
//    kalıyordu. DB gerçek kaynak olduğu için çıkarılan bir oturum sonraki erişimde eksiksiz
//    geri yüklenir; ama KULLANIMDAKİ bir oturum çıkarılmamalı — çağıranlar aynı sessionId için
//    aynı nesne referansını kilit ve durum taşıyıcısı olarak kullanıyor.

using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Outbound.Locking;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresSessionManagerCacheBoundTests
{
    private readonly PostgresCatalogFixture _fixture;
    private readonly IAppDistributedLock _lock =
        new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 }));

    public PostgresSessionManagerCacheBoundTests(PostgresCatalogFixture fixture) => _fixture = fixture;

    /// <summary>NoopMessageBus: pod'lar birbirini duymaz — DB tek ortak gerçek kaynaktır.</summary>
    private PostgresSessionManager NewPod(int maxCachedSessions = 2000, TimeSpan? minIdleBeforeEviction = null)
        => new(_fixture.DbFactory, _lock, new NoopMessageBus(), NullLogger<PostgresSessionManager>.Instance,
            maxCachedSessions, minIdleBeforeEviction);

    private static string NewId(string tag) => $"cache-{tag}-{Guid.NewGuid():N}";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AMiss_IsNotRemembered_ASessionCreatedLaterOnAnotherPodBecomesVisible()
    {
        var sessionId = NewId("miss");
        var podB = NewPod();

        (await podB.GetAsync(sessionId, Ct)).Should().BeNull("oturum henüz yok");

        var podA = NewPod();
        var created = await podA.GetOrCreateAsync(sessionId, Ct);
        created.State.AuthenticatedCustomerId = "1001";
        await podA.UpdateAsync(created, Ct);
        await podA.AddExchangeAsync(sessionId, "siparişim nerede", "kargoda", ct: Ct);

        var seen = await podB.GetAsync(sessionId, Ct);

        seen.Should().NotBeNull("oturum artık DB'de var; önceki ıskalama kalıcı bir 'yok' kararı olmamalı");
        seen!.State.AuthenticatedCustomerId.Should().Be("1001");
        (await podB.GetHistoryAsync(sessionId, Ct)).Should().HaveCount(2);
    }

    [Fact]
    public async Task AfterAMiss_GetOrCreateOnTheOtherPod_DoesNotWipeTheStoredState()
    {
        var sessionId = NewId("wipe");
        var podB = NewPod();
        _ = await podB.GetAsync(sessionId, Ct);

        var podA = NewPod();
        var created = await podA.GetOrCreateAsync(sessionId, Ct);
        created.State.AuthenticatedCustomerId = "1001";
        await podA.UpdateAsync(created, Ct);

        var onB = await podB.GetOrCreateAsync(sessionId, Ct);

        onB.State.AuthenticatedCustomerId.Should().Be("1001",
            "B, var olan oturumu DB'den okumalı — boş bir nesne üretip sahipliği ezmemeli");
        (await NewPod().GetAsync(sessionId, Ct))!.State.AuthenticatedCustomerId.Should().Be("1001",
            "DB'deki state korunmalı");
    }

    /// <summary>
    /// Oluşturma yazması başarısız olursa (burada: kolon sınırını aşan id) oturum cache'te
    /// KALMAMALI. Kalırsa sonraki çağrılar hiç kaydedilmemiş bir nesneyle "başarılı" devam eder.
    /// </summary>
    [Fact]
    public async Task WhenTheCreateWriteFails_TheSessionIsNotLeftInTheCache()
    {
        var sessionId = new string('x', 65); // chat.sessions.session_id varchar(64)
        var pod = NewPod();

        var create = () => pod.GetOrCreateAsync(sessionId, Ct);
        await create.Should().ThrowAsync<Exception>();

        (await pod.GetAsync(sessionId, Ct)).Should().BeNull("kaydedilemeyen oturum cache'te kalmamalı");
    }

    [Fact]
    public async Task OverCapacity_TheLeastRecentlyUsedSessionIsEvicted_AndReloadsIntactFromDb()
    {
        var pod = NewPod(maxCachedSessions: 2, minIdleBeforeEviction: TimeSpan.Zero);

        var oldest = await pod.GetOrCreateAsync(NewId("lru-a"), Ct);
        oldest.State.AuthenticatedCustomerId = "1001";
        await pod.UpdateAsync(oldest, Ct);
        await pod.AddExchangeAsync(oldest.SessionId, "soru", "cevap", ct: Ct);
        _ = await pod.GetOrCreateAsync(NewId("lru-b"), Ct);
        var newest = await pod.GetOrCreateAsync(NewId("lru-c"), Ct);

        var reloaded = await pod.GetAsync(oldest.SessionId, Ct);

        reloaded.Should().NotBeSameAs(oldest, "kapasite aşıldığında en uzun süredir kullanılmayan oturum cache'ten çıkmalı");
        reloaded!.State.AuthenticatedCustomerId.Should().Be("1001", "çıkarılan oturum DB'den eksiksiz geri yüklenmeli");
        (await pod.GetHistoryAsync(oldest.SessionId, Ct)).Should().HaveCount(2);
        (await pod.GetAsync(newest.SessionId, Ct)).Should().BeSameAs(newest, "en son kullanılan oturum cache'te kalmalı");
    }

    /// <summary>
    /// Bir tur oturumu dakikalarca kullanır ve aynı nesneyi kilit/durum taşıyıcısı olarak tutar.
    /// Kapasite aşılsa bile yakın zamanda erişilmiş oturum çıkarılmamalı — çıkarılırsa sonraki
    /// erişim DB'den YENİ bir nesne üretir ve turun elindeki referans sessizce eskir.
    /// Sınır geçici olarak aşılır; bellek tavanı değil, aktif turun doğruluğu önceliklidir.
    /// </summary>
    [Fact]
    public async Task RecentlyUsedSessions_AreNotEvicted_EvenOverCapacity()
    {
        var pod = NewPod(maxCachedSessions: 2, minIdleBeforeEviction: TimeSpan.FromHours(1));

        var sessions = new List<AgentSession>();
        for (var i = 0; i < 4; i++)
            sessions.Add(await pod.GetOrCreateAsync(NewId($"active-{i}"), Ct));

        foreach (var s in sessions)
            (await pod.GetAsync(s.SessionId, Ct)).Should().BeSameAs(s,
                "boşta kalma eşiğini aşmamış oturum cache'ten çıkarılmamalı");
    }
}

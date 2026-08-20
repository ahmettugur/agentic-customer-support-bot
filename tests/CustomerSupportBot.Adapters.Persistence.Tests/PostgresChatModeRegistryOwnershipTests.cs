// Oturum devralma (takeover) SAHİPLİĞİ — çoklu pod.
//
// Devralma bir "oku–karar ver–yaz" dizisidir ve kararı hangi kaynağın verdiği belirleyicidir.
// Distributed lock yalnızca EŞ ZAMANLI çağrıları sıraya sokar; bir pod'un cache'inin BAYAT
// olmasını engellemez. Devralma bilgisi pod'lara Redis pub/sub ile ulaşır ve pub/sub en fazla
// bir kez teslim eder — mesajı kaçıran pod cache'inde hiçbir sahip görmez, devralmayı kabul
// eder ve koşulsuz UPSERT ile aktif admin'i ezer.
//
// Buradaki "Redis mesajı kayboldu" modeli, iki pod'u AYRI hub'lara bağlamaktır. Aynı
// veritabanını paylaşırlar, gerçek kurulumdaki gibi.

using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Outbound.Locking;
using CustomerSupportBot.Application.Ports.Outbound.Messaging;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresChatModeRegistryOwnershipTests
{
    private readonly PostgresCatalogFixture _fixture;

    public PostgresChatModeRegistryOwnershipTests(PostgresCatalogFixture fixture) => _fixture = fixture;

    // Kilit PAYLAŞILIR: gerçek kurulumda Redis üzerinden tüm pod'lar aynı kilidi görür.
    // Bulgunun konusu kilidin yokluğu değil, kilit ALTINDA bayat cache'ten karar verilmesi.
    private readonly IAppDistributedLock _sharedLock =
        new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 }));

    private PostgresChatModeRegistry NewPod(IMessageBusPort bus) => new(
        _fixture.DbFactory, bus, _sharedLock, NullLogger<PostgresChatModeRegistry>.Instance);

    /// <summary>
    /// ASIL BULGU. A pod'unda "alice" devralır; yayın kaybolduğu için B pod'u bunu duymaz.
    /// B'ye gelen "bob" isteği reddedilmelidir — kabul edilirse alice'in aktif oturumu
    /// sessizce elinden alınır ve iki admin aynı müşteriye yazmaya başlar.
    /// </summary>
    [Fact]
    public void TakeOverOnAStalePod_DoesNotStealAnActiveSession()
    {
        var sessionId = $"takeover-{Guid.NewGuid():N}";
        var podA = NewPod(new InMemoryMessageBusHub().CreateNode());
        var podB = NewPod(new InMemoryMessageBusHub().CreateNode());

        // B'yi ÖNCE hydrate et: devralmadan ÖNCEKİ dünyayı görmüş olsun. Kritik olan bu —
        // hydrate bir kez çalışır, sonrasında cache yalnızca pub/sub ile güncellenir.
        podB.GetMode(sessionId);

        podA.TakeOver(sessionId, "alice").Should().BeTrue();

        podB.TakeOver(sessionId, "bob").Should().BeFalse(
            "B'nin cache'i bayat olsa da sahiplik kararı kayıtların gerçek kaynağından verilmeli");

        podA.GetState(sessionId)!.HumanAgent.Should().Be("alice");
    }

    /// <summary>
    /// Aynı bayatlık serbest bırakmada da geçerli: B, alice'in aktif oturumundan habersizdir
    /// ama onu serbest bırakabilmemelidir — bırakırsa müşteri, admin hâlâ konuşurken bota döner.
    /// </summary>
    [Fact]
    public void ReleaseOnAStalePod_ReflectsTheRealOwnershipState()
    {
        var sessionId = $"release-{Guid.NewGuid():N}";
        var podA = NewPod(new InMemoryMessageBusHub().CreateNode());
        var podB = NewPod(new InMemoryMessageBusHub().CreateNode());

        podB.GetMode(sessionId);   // B bayatlar: devralmadan önceki dünyayı gördü
        podA.TakeOver(sessionId, "alice").Should().BeTrue();

        // B'nin cache'i bu oturumu hiç bilmiyor. Eskiden bu, "kayıt yok → false" ile
        // sessizce geçiliyordu; artık karar DB'den okunuyor.
        podB.Release(sessionId).Should().BeTrue("oturum gerçekte insan modunda");

        // Ve serbest bırakma gerçekten kalıcı oldu.
        NewPod(new InMemoryMessageBusHub().CreateNode())
            .GetMode(sessionId).Should().Be(ChatMode.Bot);
    }

    /// <summary>Aynı admin'in kendi oturumunu yeniden devralması engellenmemeli.</summary>
    [Fact]
    public void TheSameAgentCanReacquireItsOwnSession()
    {
        var sessionId = $"reacquire-{Guid.NewGuid():N}";
        var pod = NewPod(new InMemoryMessageBusHub().CreateNode());

        pod.TakeOver(sessionId, "alice").Should().BeTrue();
        pod.TakeOver(sessionId, "alice").Should().BeTrue();
    }

    /// <summary>Serbest bırakıldıktan sonra başka bir admin devralabilmeli.</summary>
    [Fact]
    public void AfterRelease_AnotherAgentCanTakeOver()
    {
        var sessionId = $"handoff-{Guid.NewGuid():N}";
        var podA = NewPod(new InMemoryMessageBusHub().CreateNode());
        var podB = NewPod(new InMemoryMessageBusHub().CreateNode());

        podB.GetMode(sessionId);   // B bayatlar
        podA.TakeOver(sessionId, "alice").Should().BeTrue();
        podA.Release(sessionId).Should().BeTrue();

        podB.TakeOver(sessionId, "bob").Should().BeTrue();
        podB.GetState(sessionId)!.HumanAgent.Should().Be("bob");
    }
}

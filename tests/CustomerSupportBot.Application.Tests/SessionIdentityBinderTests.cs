// Oturum ↔ müşteri bağının ve sahiplik kontrolünün testleri.
//
// sessionId HER ZAMAN istemciden gelir (URL ya da gövde). Kimlik doğrulama "bu kişi bir
// müşteri mi" sorusunu yanıtlar, "bu oturum onun mu" sorusunu değil. İkinci soru uzun süre
// hiç sorulmuyordu: müşteri B, müşteri A'nın sessionId'sini vererek A'nın konuşma geçmişini
// alabiliyor ve tool'ları A adına çalıştırabiliyordu.

using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Tests.Shared;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Chat;
using Microsoft.Extensions.Options;
using CustomerSupportBot.Adapters.Persistence.InMemory;

namespace CustomerSupportBot.Application.Tests;

public class SessionIdentityBinderTests
{
    private static ISessionManager NewSessions() =>
        new InMemorySessionManager(
            new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 })));

    private static async Task<ISessionManager> SessionsWithOwner(string sessionId, string? owner)
    {
        var sessions = NewSessions();
        var session = await sessions.GetOrCreateAsync(sessionId, CancellationToken.None);
        session.State.AuthenticatedCustomerId = owner;
        await sessions.UpdateAsync(session, CancellationToken.None);
        return sessions;
    }

    // ── TryBindAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task TryBind_UnboundSession_BindsAndPersists()
    {
        var sessions = NewSessions();
        var session = await sessions.GetOrCreateAsync("s1", CancellationToken.None);

        var ok = await SessionIdentityBinder.TryBindAsync(session, "1027", sessions);

        ok.Should().BeTrue();
        var reloaded = await sessions.GetAsync("s1", CancellationToken.None);
        reloaded!.State.AuthenticatedCustomerId.Should().Be("1027");
    }

    [Fact]
    public async Task TryBind_SameCustomer_IsIdempotent()
    {
        var sessions = await SessionsWithOwner("s1", "1027");
        var session = await sessions.GetOrCreateAsync("s1", CancellationToken.None);

        var ok = await SessionIdentityBinder.TryBindAsync(session, "1027", sessions);

        ok.Should().BeTrue();
        session.State.AuthenticatedCustomerId.Should().Be("1027");
    }

    [Fact]
    public async Task TryBind_DifferentCustomer_IsRejectedAndOwnerUnchanged()
    {
        var sessions = await SessionsWithOwner("s1", "1027");
        var session = await sessions.GetOrCreateAsync("s1", CancellationToken.None);

        var ok = await SessionIdentityBinder.TryBindAsync(session, "9999", sessions);

        ok.Should().BeFalse();
        var reloaded = await sessions.GetAsync("s1", CancellationToken.None);
        reloaded!.State.AuthenticatedCustomerId.Should().Be("1027", "sahip devralınamaz");
    }

    [Fact]
    public async Task TryBind_NoIdentity_DoesNotBindButIsAllowed()
    {
        // Kimliksiz akış reddedilmez — tool'lar zaten kimliksiz çalışamaz, kendi
        // doğrulamalarında reddederler.
        var sessions = NewSessions();
        var session = await sessions.GetOrCreateAsync("s1", CancellationToken.None);

        var ok = await SessionIdentityBinder.TryBindAsync(session, null, sessions);

        ok.Should().BeTrue();
        session.State.AuthenticatedCustomerId.Should().BeNull();
    }

    // ── IsAccessibleAsync (salt-okunur uçlar) ─────────────────────────────────

    [Fact]
    public async Task IsAccessible_ForeignSession_IsFalse()
    {
        var sessions = await SessionsWithOwner("s1", "1027");

        (await SessionIdentityBinder.IsAccessibleAsync("s1", "9999", sessions)).Should().BeFalse();
    }

    [Fact]
    public async Task IsAccessible_OwnSession_IsTrue()
    {
        var sessions = await SessionsWithOwner("s1", "1027");

        (await SessionIdentityBinder.IsAccessibleAsync("s1", "1027", sessions)).Should().BeTrue();
    }

    [Fact]
    public async Task IsAccessible_UnknownSession_IsTrueAndDoesNotCreateIt()
    {
        // Salt-okunur uçlar oturum ÜRETMEMELİ; aksi hâlde rastgele sessionId veren biri
        // sınırsız boş oturum yaratabilirdi.
        var sessions = NewSessions();

        (await SessionIdentityBinder.IsAccessibleAsync("yok-boyle", "1027", sessions)).Should().BeTrue();
        (await sessions.GetAsync("yok-boyle", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task IsAccessible_UnboundSession_IsTrue()
    {
        // Henüz kimseye bağlı değil — ilk temas onu çağırana bağlayacak.
        var sessions = await SessionsWithOwner("s1", null);

        (await SessionIdentityBinder.IsAccessibleAsync("s1", "1027", sessions)).Should().BeTrue();
    }

    [Fact]
    public async Task IsAccessible_NoSessionId_IsTrue()
    {
        // Yeni sohbet: istemci henüz bir sessionId taşımıyor.
        (await SessionIdentityBinder.IsAccessibleAsync(null, "1027", NewSessions())).Should().BeTrue();
    }
}

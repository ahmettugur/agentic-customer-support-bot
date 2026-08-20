// Tests/Services/RealtimeNativeIdentityTests.cs
// Sesli (native) kanal, login'li müşterinin adını ve bugünün tarihini oturum talimatlarına
// almalı. Yazılı kanalda bu bilgi mesaj listesine system mesajı olarak giriyor; sesli modda
// mesaj listesi olmadığı için tek enjeksiyon noktası ConfigureNativeSessionAsync'tir ve bu
// uzun süre HİÇ beslenmiyordu (sesli asistan müşterinin adını da tarihi de bilmiyordu).

using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Tests.Shared;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Providers;
using CustomerSupportBot.Application.Services.Realtime;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Inbound;

namespace CustomerSupportBot.Application.Tests;

public class RealtimeNativeIdentityTests
{
    private static IBrowserChannel ClosedChannel()
    {
        var channel = Substitute.For<IBrowserChannel>();
        channel.ReceiveMessagesAsync(Arg.Any<CancellationToken>()).Returns(Empty<BrowserMessage>());
        return channel;
    }

    private static IRealtimeVoiceTransport ConnectedTransport()
    {
        var client = Substitute.For<IRealtimeVoiceTransport>();
        client.IsEnabled.Returns(true);
        client.TryConnectAsync(Arg.Any<CancellationToken>()).Returns(true);
        client.NativeToolNames.Returns([]);
        client.ReceiveEventsAsync(Arg.Any<CancellationToken>()).Returns(Empty<RealtimeServerEvent>());
        return client;
    }

    private static async IAsyncEnumerable<T> Empty<T>()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async Task<(IRealtimeVoiceTransport Client, ISessionManager Sessions)> RunAsync(
        string? authenticatedCustomerId, string? fullName)
    {
        var customers = Substitute.For<ICustomerRepository>();
        if (fullName is not null)
            customers.GetFullNameAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(fullName);

        // Kimlik ARTIK önceden session'a yazılmıyor: gerçek akışta da olduğu gibi
        // RunAsync'e parametre olarak geçilip orada bağlanmalı. Eskiden bu bağ hiç
        // kurulmuyordu ve sesli kanalda her sipariş sorgusu customerId="" ile koşuyordu.
        var sessions = new InMemorySessionManager(
            new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 })));

        var client = ConnectedTransport();
        var guard = Substitute.For<IInputGuard>();

        var svc = new RealtimeNativeService(
            client,
            sessions,
            TestFactory.CreateToolsService(
                Substitute.For<IProductCatalogRepository>(),
                Substitute.For<IOrderRepository>(),
                Substitute.For<IComplaintRepository>()),
            guard,
            Substitute.For<IChatBridge>(),
            new CustomerIdentityHintBuilder(customers),
            new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 })),
            NullLogger<RealtimeNativeService>.Instance);

        await svc.RunAsync(ClosedChannel(), "voice-1", authenticatedCustomerId, CancellationToken.None);
        return (client, sessions);
    }

    [Fact]
    public async Task LoggedInCustomer_NameReachesVoiceSessionInstructions()
    {
        var (client, _) = await RunAsync(authenticatedCustomerId: "1027", fullName: "Ahmet Tügür");

        await client.Received(1).ConfigureNativeSessionAsync(
            Arg.Is<string?>(ctx => ctx != null && ctx.Contains("Ahmet Tügür")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnonymousSession_StillReceivesDateContext()
    {
        // Kimlik yoksa bile tarih gitmeli — "yarın kargoya verilir mi" gibi ifadeler için.
        var (client, _) = await RunAsync(authenticatedCustomerId: null, fullName: null);

        await client.Received(1).ConfigureNativeSessionAsync(
            Arg.Is<string?>(ctx => !string.IsNullOrWhiteSpace(ctx) && ctx.Contains("Bugünün tarihi")),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Asıl regresyon: kimlik oturuma YAZILMALI. Sipariş tool'ları
    /// (order_status/get_last_order/get_all_orders) <c>session.State.AuthenticatedCustomerId</c>
    /// okuyor; boş kalırsa sahiplik kontrolü her siparişi reddediyor ve kullanıcı sesli
    /// asistandan "sipariş bulunamadı" duyuyor.
    /// </summary>
    [Fact]
    public async Task LoggedInCustomer_IsBoundToSession()
    {
        var (_, sessions) = await RunAsync(authenticatedCustomerId: "1027", fullName: "Ahmet Tügür");

        var session = await sessions.GetOrCreateAsync("voice-1", CancellationToken.None);
        session.State.AuthenticatedCustomerId.Should().Be("1027");
    }

    [Fact]
    public async Task SessionOwnedByAnotherCustomer_IsRejectedBeforeConfiguringVoiceSession()
    {
        // Sesli kanal artık kimlik doğruluyor; başkasının sessionId'siyle bağlanan biri
        // o oturumun kimliğiyle sipariş geçmişini dinleyebilirdi.
        var sessions = new InMemorySessionManager(
            new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 })));
        var session = await sessions.GetOrCreateAsync("voice-1", CancellationToken.None);
        session.State.AuthenticatedCustomerId = "1027";
        await sessions.UpdateAsync(session, CancellationToken.None);

        var client = ConnectedTransport();
        var svc = new RealtimeNativeService(
            client,
            sessions,
            TestFactory.CreateToolsService(
                Substitute.For<IProductCatalogRepository>(),
                Substitute.For<IOrderRepository>(),
                Substitute.For<IComplaintRepository>()),
            Substitute.For<IInputGuard>(),
            Substitute.For<IChatBridge>(),
            new CustomerIdentityHintBuilder(Substitute.For<ICustomerRepository>()),
            new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 })),
            NullLogger<RealtimeNativeService>.Instance);

        await svc.RunAsync(ClosedChannel(), "voice-1", "9999", CancellationToken.None);

        // Sesli oturum hiç yapılandırılmamalı — bağlantı kimlik kontrolünde kesilir.
        await client.DidNotReceive().ConfigureNativeSessionAsync(
            Arg.Any<string?>(), Arg.Any<CancellationToken>());

        // Ve oturumun sahibi değişmemeli.
        var reloaded = await sessions.GetOrCreateAsync("voice-1", CancellationToken.None);
        reloaded.State.AuthenticatedCustomerId.Should().Be("1027");
    }

    /// <summary>
    /// Sesli kanal, oturumu <b>kalıcı depodan tazeleyerek</b> bağlamalı — yazılı sohbetle
    /// aynı atomik mekanizma.
    ///
    /// <para>
    /// <c>GetOrCreateAsync</c> cache-first çalışır; başka bir pod oturumu bağladıysa uzak
    /// güncelleme cache'e YENİ bir nesne koyar ve elde tutulan referans eskir. Realtime
    /// tarafı bir süre bu korumadan yoksundu: yazılı sohbet düzeltilirken sesli kanallar
    /// atlanmıştı, yani aynı savunmanın ikinci kopyası eksik kalmıştı.
    /// </para>
    /// </summary>
    [Fact]
    public async Task VoiceBind_ReadsAuthoritativeSessionState_NotAStaleCachedObject()
    {
        var sessions = Substitute.For<ISessionManager>();

        // Elde tutulan (bayat) nesne: henüz kimseye bağlı değil.
        var stale = new AgentSession { SessionId = "voice-1" };
        // Kalıcı depodaki gerçek durum: oturum başka bir müşteriye ait.
        var authoritative = new AgentSession
        {
            SessionId = "voice-1",
            State = new SessionState { AuthenticatedCustomerId = "1027" }
        };

        sessions.GetOrCreateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(stale);
        sessions.ReloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(authoritative);

        var client = ConnectedTransport();
        var svc = new RealtimeNativeService(
            client,
            sessions,
            TestFactory.CreateToolsService(
                Substitute.For<IProductCatalogRepository>(),
                Substitute.For<IOrderRepository>(),
                Substitute.For<IComplaintRepository>()),
            Substitute.For<IInputGuard>(),
            Substitute.For<IChatBridge>(),
            new CustomerIdentityHintBuilder(Substitute.For<ICustomerRepository>()),
            new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 })),
            NullLogger<RealtimeNativeService>.Instance);

        await svc.RunAsync(ClosedChannel(), "voice-1", "9999", CancellationToken.None);

        // Bayat nesne kullanılsaydı oturum "bağsız" görünür ve 9999 bağlanırdı.
        await client.DidNotReceive().ConfigureNativeSessionAsync(
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
        authoritative.State.AuthenticatedCustomerId.Should().Be("1027");
    }
}

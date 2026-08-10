// Tests/Services/RealtimeNativeIdentityTests.cs
// Sesli (native) kanal, login'li müşterinin adını ve bugünün tarihini oturum talimatlarına
// almalı. Yazılı kanalda bu bilgi mesaj listesine system mesajı olarak giriyor; sesli modda
// mesaj listesi olmadığı için tek enjeksiyon noktası ConfigureNativeSessionAsync'tir ve bu
// uzun süre HİÇ beslenmiyordu (sesli asistan müşterinin adını da tarihi de bilmiyordu).

using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Api.Tests.Helpers;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Providers;
using CustomerSupportBot.Application.Services.Realtime;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Tests.Services;

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

        var sessions = new InMemorySessionManager(
            new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 })));
        var session = await sessions.GetOrCreateAsync("voice-1", CancellationToken.None);
        session.State.AuthenticatedCustomerId = authenticatedCustomerId;
        await sessions.UpdateAsync(session, CancellationToken.None);

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
            NullLogger<RealtimeNativeService>.Instance);

        await svc.RunAsync(ClosedChannel(), "voice-1", CancellationToken.None);
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
}

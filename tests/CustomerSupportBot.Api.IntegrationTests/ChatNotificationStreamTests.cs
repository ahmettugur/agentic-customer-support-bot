// Sohbet içi bildirimler: açık oturum akışına `notification` olayı olarak iner; yalnızca
// akışı açan müşterinin bildirimleri yazılır ve yetki kontrolü bu olaylar için de geçerlidir.

using System.Text;
using System.Threading.Channels;
using CustomerSupportBot.Api.Infrastructure;
using CustomerSupportBot.Api.Services;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Services.Notifications;
using CustomerSupportBot.Domain.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Api.IntegrationTests;

public class ChatNotificationStreamTests
{
    private sealed class NoopSubscription : IHitlEventSubscription { public void Dispose() { } }

    private static (ChatEventOrchestrator Orchestrator, Channel<ChatBridgeMessage> Bridge, CustomerNotificationHub Hub) Build()
    {
        var bridge = Channel.CreateUnbounded<ChatBridgeMessage>();
        var chatSession = Substitute.For<IChatSessionPort>();
        chatSession.GetStateOrDefault(Arg.Any<string>()).Returns(new ChatSessionState { Mode = ChatMode.Bot });
        chatSession.GetOpenEscalations().Returns([]);
        chatSession.SubscribeToUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => bridge.Reader.ReadAllAsync(ci.ArgAt<CancellationToken>(1)));
        var hitl = Substitute.For<IHitlEventPort>();
        hitl.SubscribeToChatEvents(Arg.Any<string>(), Arg.Any<Func<string, object, Task>>()).Returns(new NoopSubscription());
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(CancellationToken.None);

        var hub = new CustomerNotificationHub(null, NullLogger<CustomerNotificationHub>.Instance);
        return (new ChatEventOrchestrator(chatSession, hitl, lifetime, NullLogger<ChatEventOrchestrator>.Instance, hub), bridge, hub);
    }

    private static ChatBridgeMessage Marker(string text) => new()
    {
        Id = Guid.NewGuid().ToString("N"), Sender = ChatBridgeSender.Admin, HumanAgent = "admin",
        Text = text, Timestamp = DateTime.UtcNow
    };

    private static CustomerNotification Note(string text) =>
        new(CustomerNotificationKinds.OrderShipped, "Kargoya verildi", text, "1044", DateTime.UtcNow);

    private static async Task WaitForAsync(MemoryStream body, string needle)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (Encoding.UTF8.GetString(body.ToArray()).Contains(needle)) return;
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        throw new TimeoutException($"'{needle}' akışa yazılmadı");
    }

    private static async Task<string> RunAsync(
        string? customerId, Func<CustomerNotificationHub, Task> act,
        Func<CancellationToken, Task<bool>>? authorized = null, string? expect = null)
    {
        var (orchestrator, bridge, hub) = Build();
        var ctx = new DefaultHttpContext();
        var body = new MemoryStream();
        ctx.Response.Body = body;
        using var sse = new SseForwarder(ctx.Response, CancellationToken.None);

        var run = orchestrator.ExecuteAsync("s-1", sse, authorized ?? (_ => Task.FromResult(true)),
            CancellationToken.None, customerId);

        // Köprü mesajı geldiyse abonelikler çoktan kurulmuştur.
        await bridge.Writer.WriteAsync(Marker("hazir"), TestContext.Current.CancellationToken);
        await WaitForAsync(body, "hazir");
        await act(hub);
        // Beklenen içerik varsa onu bekle; yoksa (olumsuz senaryo) yazım için kısa bir süre tanı.
        if (expect is not null) await WaitForAsync(body, expect);
        else await Task.Delay(300, TestContext.Current.CancellationToken);
        bridge.Writer.Complete();
        await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        return Encoding.UTF8.GetString(body.ToArray());
    }

    [Fact]
    public async Task TheCustomersNotification_IsWrittenAsANotificationEvent()
    {
        var written = await RunAsync("1001", hub => { hub.Publish("1001", Note("siparis-kargoda")); return Task.CompletedTask; },
            expect: "siparis-kargoda");

        written.Should().Contain("event: notification").And.Contain("siparis-kargoda");
    }

    [Fact]
    public async Task AnotherCustomersNotification_NeverReachesTheStream()
    {
        var written = await RunAsync("1001", hub => { hub.Publish("2002", Note("BASKASININ")); return Task.CompletedTask; });

        written.Should().NotContain("BASKASININ");
    }

    [Fact]
    public async Task AnAnonymousStream_GetsNoNotifications()
    {
        var written = await RunAsync(null, hub => { hub.Publish("1001", Note("SIZMAMALI")); return Task.CompletedTask; });

        written.Should().NotContain("SIZMAMALI");
    }

    [Fact]
    public async Task WhenOwnershipHasMoved_TheNotificationIsNotWritten()
    {
        var calls = 0;
        // İlk kontrol (oturum bilgisi yazımı sonrası ilk olay) geçer, sonrakiler reddeder.
        var written = await RunAsync("1001",
            hub => { hub.Publish("1001", Note("SIZMAMALI")); return Task.CompletedTask; },
            _ => Task.FromResult(Interlocked.Increment(ref calls) <= 1));

        written.Should().NotContain("SIZMAMALI");
    }
}

// Açık SSE akışının YENİDEN YETKİLENDİRİLMESİ.
//
// Olay akışına abone olurken yapılan sahiplik kontrolü, henüz kimseye BAĞLI OLMAYAN bir
// oturuma aboneliğe izin verir — ilk temasın oturumu çağırana bağlaması için bu gerekli.
// Ama oturum daha sonra BAŞKA bir müşteriye bağlanabilir. Açık akış o anda yeniden
// yetkilendirilmezse, o müşterinin bot yanıtları, temsilci mesajları ve onay sonuçları
// ilk aboneye akmaya devam eder — kurban hiçbir şey fark etmeden.
//
// Bu yüzden kontrol tek seferlik değil, her olay yazımından önce tekrarlanır.

using System.Threading.Channels;
using CustomerSupportBot.Api.Infrastructure;
using CustomerSupportBot.Api.Services;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Domain.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Api.IntegrationTests;

public class ChatEventStreamReauthorizationTests
{
    private sealed class NoopSubscription : IHitlEventSubscription
    {
        public void Dispose() { }
    }

    private static (ChatEventOrchestrator Orchestrator, Channel<ChatBridgeMessage> Bridge)
        Build(IChatSessionPort? sessionPort = null)
    {
        var bridge = Channel.CreateUnbounded<ChatBridgeMessage>();

        var chatSession = sessionPort ?? Substitute.For<IChatSessionPort>();
        chatSession.GetStateOrDefault(Arg.Any<string>())
            .Returns(new ChatSessionState { Mode = ChatMode.Bot });
        chatSession.GetOpenEscalations().Returns([]);
        chatSession.SubscribeToUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => bridge.Reader.ReadAllAsync(ci.ArgAt<CancellationToken>(1)));

        var hitl = Substitute.For<IHitlEventPort>();
        hitl.SubscribeToChatEvents(Arg.Any<string>(), Arg.Any<Func<string, object, Task>>())
            .Returns(new NoopSubscription());

        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(CancellationToken.None);

        return (new ChatEventOrchestrator(
            chatSession, hitl, lifetime, NullLogger<ChatEventOrchestrator>.Instance), bridge);
    }

    private static (HttpResponse Response, MemoryStream Body) NewResponse()
    {
        var ctx = new DefaultHttpContext();
        var body = new MemoryStream();
        ctx.Response.Body = body;
        return (ctx.Response, body);
    }

    private static ChatBridgeMessage AdminMessage(string text) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Sender = ChatBridgeSender.Admin,
        HumanAgent = "admin",
        Text = text,
        Timestamp = DateTime.UtcNow
    };

    /// <summary>
    /// ASIL BULGU. Abonelik açılırken oturum sahipsizdir (izin verilir); sonra oturum başka
    /// bir müşteriye bağlanır. O andan itibaren hiçbir olay bu akışa yazılmamalıdır.
    /// </summary>
    [Fact]
    public async Task WhenOwnershipMovesToAnotherCustomer_TheStreamStopsEmitting()
    {
        var (orchestrator, bridge) = Build();
        var (response, body) = NewResponse();

        // İlk kontrol izin verir (oturum sahipsiz), sonrakiler reddeder (oturum başkasına bağlandı).
        var calls = 0;
        Func<CancellationToken, Task<bool>> stillAuthorized =
            _ => Task.FromResult(Interlocked.Increment(ref calls) <= 1);

        using var sse = new SseForwarder(response, CancellationToken.None);
        var run = orchestrator.ExecuteAsync("s-1", sse, stillAuthorized, CancellationToken.None);

        await bridge.Writer.WriteAsync(AdminMessage("kurbanin ilk mesaji"));
        await bridge.Writer.WriteAsync(AdminMessage("SIZMAMALI"));
        bridge.Writer.Complete();

        await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var written = System.Text.Encoding.UTF8.GetString(body.ToArray());
        written.Should().NotContain("SIZMAMALI",
            "sahiplik değiştikten sonra hiçbir olay bu aboneye ulaşmamalı");
    }

    /// <summary>
    /// Kontrol en baştan reddediyorsa (oturum zaten başkasına bağlı) hiçbir içerik olayı
    /// yazılmamalı.
    /// </summary>
    [Fact]
    public async Task WhenNeverAuthorized_NoEventContentIsWritten()
    {
        var (orchestrator, bridge) = Build();
        var (response, body) = NewResponse();

        using var sse = new SseForwarder(response, CancellationToken.None);
        var run = orchestrator.ExecuteAsync(
            "s-2", sse, _ => Task.FromResult(false), CancellationToken.None);

        await bridge.Writer.WriteAsync(AdminMessage("SIZMAMALI"));
        bridge.Writer.Complete();

        await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        System.Text.Encoding.UTF8.GetString(body.ToArray())
            .Should().NotContain("SIZMAMALI");
    }

    /// <summary>
    /// Karşı yön: sahiplik korunduğu sürece akış normal çalışmalı — koruma meşru
    /// bağlantıyı kesmemelidir.
    /// </summary>
    [Fact]
    public async Task WhileOwnershipHolds_EventsAreDelivered()
    {
        var (orchestrator, bridge) = Build();
        var (response, body) = NewResponse();

        using var sse = new SseForwarder(response, CancellationToken.None);
        var run = orchestrator.ExecuteAsync(
            "s-3", sse, _ => Task.FromResult(true), CancellationToken.None);

        await bridge.Writer.WriteAsync(AdminMessage("merhaba-ulasmali"));
        bridge.Writer.Complete();

        await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        System.Text.Encoding.UTF8.GetString(body.ToArray())
            .Should().Contain("merhaba-ulasmali");
    }
}

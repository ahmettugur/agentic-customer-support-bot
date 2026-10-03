// Native sesli kanalda DUYGU DEĞERLENDİRMESİ ve ESKALASYON eşdeğerliği.
//
// Metin kanalında tur sonunda duygu uyarısı değerlendirilir ve eskalasyon, workflow'daki
// uzman ajanın "needs_escalation" kararından gelir. Native sesli modda workflow yoktur;
// bu yüzden ne uyarı değerlendiriliyor ne de eskalasyona giden bir yol vardı — art arda
// olumsuz turlar yaşayan sesli müşteri insana ulaşamıyordu.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Application.Services.Providers;
using CustomerSupportBot.Application.Services.Realtime;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class RealtimeNativeSentimentTests
{
    private const string SessionId = "voice-sentiment";

    private static async IAsyncEnumerable<T> Empty<T>()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<RealtimeServerEvent> Turns(params string[] userSaid)
    {
        foreach (var text in userSaid)
        {
            yield return new RealtimeServerEvent(RealtimeServerEventType.ResponseCreated);
            yield return new RealtimeServerEvent(RealtimeServerEventType.InputTranscriptCompleted) { Transcript = text };
            yield return new RealtimeServerEvent(RealtimeServerEventType.AssistantTextDelta) { TextDelta = "Üzgünüm, yardımcı olayım." };
            yield return new RealtimeServerEvent(RealtimeServerEventType.ResponseDone);
        }
        await Task.CompletedTask;
    }

    private static async Task<(InMemoryEscalationSink Sink, List<string> SentTypes)> RunAsync(params string[] userSaid)
    {
        var sent = new List<string>();
        var channel = Substitute.For<IBrowserChannel>();
        channel.ReceiveMessagesAsync(Arg.Any<CancellationToken>()).Returns(Empty<BrowserMessage>());
        channel.SendJsonAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var type = ci.Arg<object>().GetType().GetProperty("type")?.GetValue(ci.Arg<object>()) as string;
                if (type is not null) lock (sent) sent.Add(type);
                return Task.CompletedTask;
            });

        var client = Substitute.For<IRealtimeVoiceTransport>();
        client.IsEnabled.Returns(true);
        client.TryConnectAsync(Arg.Any<CancellationToken>()).Returns(true);
        client.NativeToolNames.Returns([]);
        client.ReceiveEventsAsync(Arg.Any<CancellationToken>()).Returns(_ => Turns(userSaid));

        var guard = Substitute.For<IInputGuard>();
        guard.Inspect(Arg.Any<string>())
            .Returns(ci => new InputGuardResult(InputGuardVerdict.Allow, ci.Arg<string>() ?? "", [], null));

        var locks = new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 }));
        var sessions = new InMemorySessionManager(locks);
        var sink = new InMemoryEscalationSink(NullLogger<InMemoryEscalationSink>.Instance);

        var svc = new RealtimeNativeService(
            client, sessions,
            TestFactory.CreateToolsService(
                Substitute.For<IProductCatalogRepository>(),
                Substitute.For<IOrderRepository>(),
                Substitute.For<IComplaintRepository>()),
            guard, Substitute.For<IChatBridge>(),
            new CustomerIdentityHintBuilder(Substitute.For<ICustomerRepository>()),
            locks,
            NullLogger<RealtimeNativeService>.Instance,
            new SessionStateService(sessions, NullLogger<SessionStateService>.Instance),
            sink);

        await svc.RunAsync(channel, SessionId, authenticatedCustomerId: null, CancellationToken.None);
        return (sink, sent);
    }

    [Fact]
    public async Task ConsecutiveNegativeVoiceTurns_RaiseAlertAndOpenOneEscalation()
    {
        var (sink, sent) = await RunAsync(
            "bu tam bir rezalet", "kargo yine gecikmeli, berbat", "kabul edilemez bir hizmet", "hala sorun var");

        sent.Should().Contain(StreamEventTypes.SentimentAlert);

        var open = sink.GetOpen();
        open.Should().ContainSingle("eşik her turda yeniden aşılsa da oturum başına tek açık eskalasyon olmalı");
        open[0].SessionId.Should().Be(SessionId);
        open[0].AgentName.Should().Be(RealtimeNativeService.VoiceAgentName);
        open[0].Priority.Should().Be(EscalationPriority.High);
    }

    [Fact]
    public async Task NeutralVoiceTurns_UpdateSentimentWithoutEscalating()
    {
        var (sink, sent) = await RunAsync("siparişim nerede", "teşekkürler");

        sent.Should().Contain(StreamEventTypes.SentimentUpdate);
        sent.Should().NotContain(StreamEventTypes.SentimentAlert);
        sink.GetOpen().Should().BeEmpty();
    }
}

// Sesli görüşmede CANLI ALTYAZI — kullanıcı konuşurken transkript parçalarının ekrana akması.
//
// Parçalar yalnızca konuşan kişinin ekranı içindir: tarayıcıya konuşma kimliğiyle (itemId)
// iletilir ama geçmişe, duygu analizine ya da onay kaydına GİRMEZ. Parçaların birleşimi son
// metne eşit olmak zorunda değildir (sağlayıcı sonradan düzeltebilir); kalıcı kayıt yalnızca
// tamamlanmış transkriptten yapılır.

using System.Text.Json;
using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Providers;
using CustomerSupportBot.Application.Services.Realtime;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class RealtimeNativeLiveCaptionTests
{
    private const string SessionId = "voice-captions";

    private static async IAsyncEnumerable<T> Stream<T>(IEnumerable<T> items)
    {
        foreach (var item in items) yield return item;
        await Task.CompletedTask;
    }

    private static async Task<(List<JsonElement> Sent, List<ConversationMessage> History)> RunAsync(
        params RealtimeServerEvent[] events)
    {
        var sent = new List<JsonElement>();
        var channel = Substitute.For<IBrowserChannel>();
        channel.ReceiveMessagesAsync(Arg.Any<CancellationToken>()).Returns(Stream<BrowserMessage>([]));
        channel.SendJsonAsync(Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            lock (sent) sent.Add(JsonSerializer.SerializeToElement(ci.Arg<object>()));
            return Task.CompletedTask;
        });

        var client = Substitute.For<IRealtimeVoiceTransport>();
        client.IsEnabled.Returns(true);
        client.TryConnectAsync(Arg.Any<CancellationToken>()).Returns(true);
        client.NativeToolNames.Returns([]);
        client.ReceiveEventsAsync(Arg.Any<CancellationToken>()).Returns(_ => Stream(events));

        var guard = Substitute.For<IInputGuard>();
        guard.Inspect(Arg.Any<string>())
            .Returns(ci => new InputGuardResult(InputGuardVerdict.Allow, ci.Arg<string>() ?? "", [], null));

        var locks = new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 }));
        var sessions = new InMemorySessionManager(locks);
        var svc = new RealtimeNativeService(
            client, sessions,
            TestFactory.CreateToolsService(
                Substitute.For<IProductCatalogRepository>(),
                Substitute.For<IOrderRepository>(),
                Substitute.For<IComplaintRepository>()),
            guard, Substitute.For<IChatBridge>(),
            new CustomerIdentityHintBuilder(Substitute.For<ICustomerRepository>()),
            locks, NullLogger<RealtimeNativeService>.Instance);

        await svc.RunAsync(channel, SessionId, authenticatedCustomerId: null, CancellationToken.None);
        return (sent, await sessions.GetHistoryAsync(SessionId, CancellationToken.None));
    }

    private static IEnumerable<JsonElement> OfType(List<JsonElement> sent, string type) =>
        sent.Where(e => e.TryGetProperty("type", out var t) && t.GetString() == type);

    private static RealtimeServerEvent Delta(string item, string text) =>
        new(RealtimeServerEventType.InputTranscriptDelta) { ItemId = item, TextDelta = text };

    [Fact]
    public async Task TranscriptDeltas_AreForwardedToTheBrowser_WithTheirItemId()
    {
        var (sent, _) = await RunAsync(
            new RealtimeServerEvent(RealtimeServerEventType.SpeechStarted) { ItemId = "U1" },
            Delta("U1", "Siparişim "),
            Delta("U1", "nerede"));

        var deltas = OfType(sent, "user_transcript_delta").ToList();
        deltas.Select(d => d.GetProperty("itemId").GetString()).Should().Equal("U1", "U1");
        deltas.Select(d => d.GetProperty("text").GetString()).Should().Equal("Siparişim ", "nerede");
    }

    [Fact]
    public async Task SpeechAndFinalTranscriptEvents_CarryTheItemId()
    {
        var (sent, _) = await RunAsync(
            new RealtimeServerEvent(RealtimeServerEventType.SpeechStarted) { ItemId = "U1" },
            new RealtimeServerEvent(RealtimeServerEventType.SpeechStopped) { ItemId = "U1" },
            new RealtimeServerEvent(RealtimeServerEventType.InputTranscriptCompleted) { ItemId = "U1", Transcript = "merhaba" });

        OfType(sent, "speech_started").Single().GetProperty("itemId").GetString().Should().Be("U1");
        OfType(sent, "speech_stopped").Single().GetProperty("itemId").GetString().Should().Be("U1");
        OfType(sent, "user_transcript").Single().GetProperty("itemId").GetString().Should().Be("U1");
    }

    [Fact]
    public async Task OnlyTheCompletedTranscript_IsPersisted_NotTheDeltas()
    {
        var (_, history) = await RunAsync(
            new RealtimeServerEvent(RealtimeServerEventType.InputAudioCommitted) { ItemId = "U1" },
            new RealtimeServerEvent(RealtimeServerEventType.ResponseCreated),
            Delta("U1", "siparişm "),            // sağlayıcı sonradan düzeltiyor
            Delta("U1", "nerde"),
            new RealtimeServerEvent(RealtimeServerEventType.InputTranscriptCompleted) { ItemId = "U1", Transcript = "siparişim nerede" },
            new RealtimeServerEvent(RealtimeServerEventType.AssistantTextDelta) { TextDelta = "Kargoda." },
            new RealtimeServerEvent(RealtimeServerEventType.ResponseDone));

        history.Select(m => m.Text).Should().Equal("siparişim nerede", "Kargoda.");
    }

    [Fact]
    public async Task EmptyDeltas_AreNotForwarded()
    {
        var (sent, _) = await RunAsync(Delta("U1", ""), Delta("U1", "a"));

        OfType(sent, "user_transcript_delta").Should().ContainSingle();
    }
}

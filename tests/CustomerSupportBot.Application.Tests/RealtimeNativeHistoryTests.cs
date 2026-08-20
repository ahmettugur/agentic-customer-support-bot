// Sesli (native) turların KONUŞMA GEÇMİŞİNE yazılması.
//
// Native turda kullanıcı tarafı geçmişe sabit bir "(sesli)" metniyle yazılıyordu — yani
// müşterinin ne sorduğu hiçbir yerde durmuyordu. İki sonucu vardı: bot moduna geçildiğinde
// ajan önceki isteği bilmiyor, temsilci devraldığında panelde müşterinin ne dediği
// görünmüyordu. Ayrıca yazma yalnızca chat bridge'e yapılıyordu; ajanın bağlamı ise
// ISessionManager'dan gelir, dolayısıyla sesli turlar konuşma geçmişinde hiç yer almıyordu.

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

public class RealtimeNativeHistoryTests
{
    private const string SessionId = "voice-history";
    private const string UserSaid = "siparişim nerede kaldı";
    private const string BotSaid = "Siparişiniz kargoda.";

    private static async IAsyncEnumerable<T> Empty<T>()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<RealtimeServerEvent> OneVoiceTurn()
    {
        yield return new RealtimeServerEvent(RealtimeServerEventType.InputTranscriptCompleted)
        {
            Transcript = UserSaid
        };
        yield return new RealtimeServerEvent(RealtimeServerEventType.AssistantTextDelta)
        {
            TextDelta = BotSaid
        };
        yield return new RealtimeServerEvent(RealtimeServerEventType.ResponseDone);
        await Task.CompletedTask;
    }

    private static async Task<(ISessionManager Sessions, IChatBridge Bridge)> RunOneTurnAsync()
    {
        var channel = Substitute.For<IBrowserChannel>();
        channel.ReceiveMessagesAsync(Arg.Any<CancellationToken>()).Returns(Empty<BrowserMessage>());

        var client = Substitute.For<IRealtimeVoiceTransport>();
        client.IsEnabled.Returns(true);
        client.TryConnectAsync(Arg.Any<CancellationToken>()).Returns(true);
        client.NativeToolNames.Returns([]);
        client.ReceiveEventsAsync(Arg.Any<CancellationToken>()).Returns(_ => OneVoiceTurn());

        var guard = Substitute.For<IInputGuard>();
        guard.Inspect(Arg.Any<string>())
            .Returns(ci => new InputGuardResult(InputGuardVerdict.Allow, ci.Arg<string>() ?? "", [], null));

        var sessions = new InMemorySessionManager(
            new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 })));
        var bridge = Substitute.For<IChatBridge>();

        var svc = new RealtimeNativeService(
            client, sessions,
            TestFactory.CreateToolsService(
                Substitute.For<IProductCatalogRepository>(),
                Substitute.For<IOrderRepository>(),
                Substitute.For<IComplaintRepository>()),
            guard, bridge,
            new CustomerIdentityHintBuilder(Substitute.For<ICustomerRepository>()),
            new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 })),
            NullLogger<RealtimeNativeService>.Instance);

        await svc.RunAsync(channel, SessionId, authenticatedCustomerId: null, CancellationToken.None);
        return (sessions, bridge);
    }

    /// <summary>
    /// ASIL BULGU. Müşterinin gerçekte söylediği metin geçmişe girmeli — sabit bir
    /// "(sesli)" etiketi değil.
    /// </summary>
    [Fact]
    public async Task VoiceTurn_PersistsTheRealUserTranscript_NotAPlaceholder()
    {
        var (sessions, _) = await RunOneTurnAsync();

        var history = await sessions.GetHistoryAsync(SessionId, TestContext.Current.CancellationToken);

        history.Should().Contain(m => m.Text.Contains(UserSaid),
            "sesli turda müşterinin ne sorduğu geçmişte durmalı");
        history.Should().NotContain(m => m.Text == "(sesli)",
            "yer tutucu, konuşmanın yarısını atmak demekti");
    }

    /// <summary>Asistanın yanıtı da geçmişe girmeli — tur tek parçadır.</summary>
    [Fact]
    public async Task VoiceTurn_PersistsTheAssistantResponse()
    {
        var (sessions, _) = await RunOneTurnAsync();

        var history = await sessions.GetHistoryAsync(SessionId, TestContext.Current.CancellationToken);

        history.Should().Contain(m => m.Text.Contains(BotSaid));
    }

    /// <summary>
    /// Admin paneli chat bridge'ten beslenir; oraya da gerçek metin gitmeli, yoksa temsilci
    /// devraldığında müşterinin ne dediğini göremez.
    /// </summary>
    [Fact]
    public async Task VoiceTurn_SendsTheRealTranscriptToTheAdminBridge()
    {
        var (_, bridge) = await RunOneTurnAsync();

        bridge.Received(1).RecordBotExchange(
            SessionId,
            Arg.Is<string>(t => t.Contains(UserSaid)),
            Arg.Is<string>(t => t.Contains(BotSaid)));
    }
}

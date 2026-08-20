// Tests/CanonicalResponsePersistenceTests.cs
//
// Konuşma geçmişine ve TTS'e giden metnin KAYNAĞI doğru mu?
//
// 🐞 Düzeltilen hata: ChatPortService ve RealtimeBridgeService, turun yanıtını response_delta
// olaylarını birleştirerek üretiyordu. Ama delta'lar ResponseAgent'ın HAM token akışıdır
// (yalnızca TERMINATE'ten kesilir); turun kanonik metni ise response_complete'te taşınır ve
// ek temizlikten geçmiştir — teknik JSON blokları silinir, yanıtta ajan adı sızıntısı varsa
// (ContainsAgentRoutingMessage) metin LLM ile TAMAMEN yeniden yazılır.
//
// Sonuç: ekranda temiz metin görünürken (response_complete baloncuğun üzerine yazar)
// veritabanına ham metin yazılıyordu; sesli kanalda ise müşteri "OrderAgent size yardımcı
// olacak" gibi bir cümleyi DUYUYORDU. Yani RewriteRoutingMessageAsync savunması yalnızca
// yazılı sohbet ekranına uygulanıyor, kalıcılığı ve sesi baypas ediyordu.
//
// Bu testler kaynağın kanonik metin olduğunu ve o hiç gelmezse delta'lara düşüldüğünü kilitler.

using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class CanonicalResponsePersistenceTests
{
    private const string RawStreamed = "OrderAgent size yardımcı olacak.";
    private const string Canonical = "Sipariş ekibimiz size yardımcı olacak.";

    /// <summary>Delta'ları HAM metni, response_complete'i KANONİK metni taşıyan sahte takım.</summary>
    private sealed class FakeTeam : IAgentTeamPort
    {
        public required IReadOnlyList<StreamEvent> Events { get; init; }

        public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(
            string query, List<ConversationMessage>? history = null, AgentSession? session = null,
            ReasoningResult? reasoning = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var e in Events) yield return e;
            await Task.CompletedTask;
        }

        public Task<string> RunAsync(string query, List<ConversationMessage>? history = null,
            AgentSession? session = null, ReasoningResult? reasoning = null, CancellationToken ct = default)
            => Task.FromResult("");

        public string GetWorkflowDiagram() => "";
    }

    private sealed class NoopReasoning : IReasoningPort
    {
        public Task<ReasoningResult> ReasonAsync(string query, AgentSession? session = null,
            List<ConversationMessage>? history = null, CancellationToken ct = default)
            => Task.FromResult(new ReasoningResult());

        public async IAsyncEnumerable<StreamEvent> ReasonStreamingAsync(
            string query, AgentSession? session = null, List<ConversationMessage>? history = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield break;
        }
    }

    private static async Task<string?> RunAndCapturePersistedAsync(params StreamEvent[] events)
    {
        var session = new AgentSession { SessionId = "s1" };

        var sessions = Substitute.For<ISessionManager>();
        sessions.GetOrCreateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(session);
        sessions.GetHistoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<ConversationMessage>());

        string? persisted = null;
        await sessions.AddExchangeAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Do<string>(r => persisted = r),
            Arg.Any<TurnSignals?>(), Arg.Any<CancellationToken>());

        var modeRepo = Substitute.For<IChatModeRegistry>();
        modeRepo.GetMode(Arg.Any<string>()).Returns(ChatMode.Bot);

        var svc = new ChatPortService(
            new FakeTeam { Events = events },
            new NoopReasoning(),
            sessions,
            modeRepo,
            Substitute.For<IChatBridge>(),
            new SessionStateService(sessions, NullLogger<SessionStateService>.Instance),
            Substitute.For<IApprovalContextAccessor>(),
            new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 5 })),
            NullLogger<ChatPortService>.Instance);

        await foreach (var _ in svc.HandleStreamAsync(new ChatRequest("merhaba", "s1"))) { }

        return persisted;
    }

    /// <summary>
    /// Asıl regresyon: delta'lar ham metni taşırken response_complete kanonik metni taşıyorsa,
    /// geçmişe KANONİK metin yazılmalı. Bu test düzeltmeden önce başarısız olurdu.
    /// </summary>
    [Fact]
    public async Task Persists_CanonicalText_NotConcatenatedRawDeltas()
    {
        var persisted = await RunAndCapturePersistedAsync(
            new StreamEvent(StreamEventTypes.ResponseStart, null),
            new StreamEvent(StreamEventTypes.ResponseDelta, new TextDeltaPayload(RawStreamed)),
            new StreamEvent(StreamEventTypes.ResponseComplete, new ResponseCompletePayload(Canonical)));

        persisted.Should().Be(Canonical);
        persisted.Should().NotContain("OrderAgent",
            "ajan adı sızıntısı geçmişe yazılırsa sonraki turlarda bağlam olarak modele geri beslenir");
    }

    /// <summary>
    /// Güvenlik ağı: response_complete hiç gelmezse (hata/iptal) eski davranış korunmalı —
    /// delta birleşimi kullanılır, yani yanıt tamamen kaybolmaz.
    /// </summary>
    [Fact]
    public async Task FallsBackToDeltas_WhenResponseCompleteNeverArrives()
    {
        var persisted = await RunAndCapturePersistedAsync(
            new StreamEvent(StreamEventTypes.ResponseStart, null),
            new StreamEvent(StreamEventTypes.ResponseDelta, new TextDeltaPayload("kısmi ")),
            new StreamEvent(StreamEventTypes.ResponseDelta, new TextDeltaPayload("yanıt")));

        persisted.Should().Be("kısmi yanıt");
    }

    /// <summary>
    /// Delta hiç gelmese bile (gerçek akış tetiklenmediği turlar) kanonik metin yazılmalı.
    /// </summary>
    [Fact]
    public async Task Persists_CanonicalText_EvenWithNoDeltasAtAll()
    {
        var persisted = await RunAndCapturePersistedAsync(
            new StreamEvent(StreamEventTypes.ResponseComplete, new ResponseCompletePayload(Canonical)));

        persisted.Should().Be(Canonical);
    }
}

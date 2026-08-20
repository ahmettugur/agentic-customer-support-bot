// Tests/VoiceCanonicalResponseTests.cs
//
// Sesli kanalda MÜŞTERİNİN DUYDUĞU metnin kaynağı doğru mu?
//
// Bu, CanonicalResponsePersistenceTests'in (yazılı kanal) sesli karşılığı ve aslında hatanın
// EN CİDDİ hâli: yazılı kanalda kullanıcı yanlış metni yalnızca kaydedilmiş hâlde görürdü
// (ekranda response_complete baloncuğun üzerine yazdığı için doğrusu görünür), sesli kanalda
// ise yanlış metin doğrudan TTS'e gidiyordu — yani müşteri "OrderAgent size yardımcı olacak"
// gibi bir cümleyi KULAĞIYLA duyuyordu. RewriteRoutingMessageAsync savunması yalnızca yazılı
// ekrana uygulanıyor, sesi baypas ediyordu.

using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Realtime;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Application.Tests;

public class VoiceCanonicalResponseTests
{
    private const string RawStreamed = "OrderAgent size yardımcı olacak.";
    private const string Canonical = "Sipariş ekibimiz size yardımcı olacak.";

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

    private sealed record Captured(string? Spoken, string? Persisted);

    private static async Task<Captured> RunVoiceTurnAsync(params StreamEvent[] events)
    {
        string? spoken = null;
        string? persisted = null;

        var voice = Substitute.For<IRealtimeVoiceTransport>();
        await voice.SpeakTextAsync(Arg.Do<string>(t => spoken = t), Arg.Any<string>(), Arg.Any<CancellationToken>());

        var sessions = Substitute.For<ISessionManager>();
        sessions.GetHistoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<ConversationMessage>());
        await sessions.AddExchangeAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Do<string>(r => persisted = r),
            Arg.Any<TurnSignals?>(), Arg.Any<CancellationToken>());

        var guard = Substitute.For<IInputGuard>();
        guard.Inspect(Arg.Any<string>())
            .Returns(ci => new InputGuardResult(InputGuardVerdict.Allow, ci.Arg<string>() ?? "", [], null));

        var approvalContext = Substitute.For<IApprovalContextAccessor>();
        approvalContext.SetScope(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(Substitute.For<IDisposable>());

        var svc = new RealtimeBridgeService(
            voice,
            new FakeTeam { Events = events },
            sessions,
            new NoopReasoning(),
            approvalContext,
            Substitute.For<IChatBridge>(),
            guard,
            new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 })),
            NullLogger<RealtimeBridgeService>.Instance);

        await svc.HandleUserTranscriptAsync(
            Substitute.For<IBrowserChannel>(),
            new AgentSession { SessionId = "s1" },
            "siparişim nerede",
            CancellationToken.None);

        return new Captured(spoken, persisted);
    }

    /// <summary>
    /// Asıl regresyon: delta'lar ham metni taşırken TTS'e KANONİK metin verilmeli.
    /// Düzeltmeden önce müşteri ajan adını sesli duyuyordu.
    /// </summary>
    [Fact]
    public async Task SpeaksCanonicalText_NotRawDeltas()
    {
        var c = await RunVoiceTurnAsync(
            new StreamEvent(StreamEventTypes.ResponseDelta, new TextDeltaPayload(RawStreamed)),
            new StreamEvent(StreamEventTypes.ResponseComplete, new ResponseCompletePayload(Canonical)));

        c.Spoken.Should().Be(Canonical);
        c.Spoken.Should().NotContain("OrderAgent",
            "müşteri iç ajan adını SESLİ duymamalı — bu, yazılı kanaldakinden daha görünür bir sızıntı");
        c.Persisted.Should().Be(Canonical);
    }

    /// <summary>
    /// Güvenlik ağı: response_complete hiç gelmezse yanıt tamamen kaybolmamalı —
    /// delta birleşimi konuşulur.
    /// </summary>
    [Fact]
    public async Task FallsBackToDeltas_WhenResponseCompleteNeverArrives()
    {
        var c = await RunVoiceTurnAsync(
            new StreamEvent(StreamEventTypes.ResponseDelta, new TextDeltaPayload("kısmi ")),
            new StreamEvent(StreamEventTypes.ResponseDelta, new TextDeltaPayload("yanıt")));

        c.Spoken.Should().Be("kısmi yanıt");
    }
}

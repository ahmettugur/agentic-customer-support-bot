// Başarısız bir turda kullanıcının MESAJI geçmişe yazılıyor mu?
//
// Yanıt, ResponseAgent hiç konuşmadan önce bir hata/timeout ile kesildiğinde elde metin
// kalmaz. Kalıcılaştırma yalnızca "yanıt boş değilse" yapıldığı için bu turda kullanıcının
// sorusu da hiç yazılmıyordu: bir sonraki tur bağlamsız başlıyor, admin panelindeki geçmişte
// müşterinin mesajı hiç görünmüyordu. Non-streaming yolda RunAsync hata fırlattığında da aynısı.
//
// Kısmi delta'ların yazılması (response_complete hiç gelmezse) BİLİNÇLİ bir tasarım kararıdır
// ve korunur — bkz. CanonicalResponsePersistenceTests.FallsBackToDeltas_WhenResponseCompleteNeverArrives.
// Bu testler yalnızca "hiç yanıt yok" durumunu kapsar.

using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Application.Services.Realtime;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class FailedTurnHistoryTests
{
    private const string Query = "siparişim nerede";

    private sealed class FakeTeam : IAgentTeamPort
    {
        public IReadOnlyList<StreamEvent> Events { get; init; } = [];
        public Exception? RunAsyncThrows { get; init; }

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
            => RunAsyncThrows is null ? Task.FromResult("yanıt") : Task.FromException<string>(RunAsyncThrows);

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

    private static readonly StreamEvent WorkflowError =
        new(StreamEventTypes.Error, new { message = "İşlem 180 saniyede tamamlanamadı." });

    private static (ChatPortService Svc, ISessionManager Sessions) NewChatPort(FakeTeam team)
    {
        var session = new AgentSession { SessionId = "s1" };
        var sessions = Substitute.For<ISessionManager>();
        sessions.GetOrCreateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(session);
        sessions.ReloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(session);
        sessions.GetHistoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<ConversationMessage>());

        var modeRepo = Substitute.For<IChatModeRegistry>();
        modeRepo.GetMode(Arg.Any<string>()).Returns(ChatMode.Bot);

        var svc = new ChatPortService(
            team, new NoopReasoning(), sessions, modeRepo, Substitute.For<IChatBridge>(),
            new SessionStateService(sessions, NullLogger<SessionStateService>.Instance),
            Substitute.For<IApprovalContextAccessor>(),
            new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 5 })),
            NullLogger<ChatPortService>.Instance);
        return (svc, sessions);
    }

    [Fact]
    public async Task Streaming_WhenTheTurnFailsWithNoResponse_TheUserMessageIsStillRecorded()
    {
        var (svc, sessions) = NewChatPort(new FakeTeam { Events = [WorkflowError] });

        await foreach (var _ in svc.HandleStreamAsync(new ChatRequest(Query, "s1"), TestContext.Current.CancellationToken)) { }

        await sessions.Received(1).AppendUserMessageAsync("s1", Query, Arg.Any<CancellationToken>());
        await sessions.DidNotReceive().AddExchangeAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TurnSignals?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NonStreaming_WhenTheTeamThrows_TheUserMessageIsStillRecorded()
    {
        var (svc, sessions) = NewChatPort(new FakeTeam { RunAsyncThrows = new InvalidOperationException("workflow hatası") });

        var act = () => svc.HandleAsync(new ChatRequest(Query, "s1"), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>("hata çağırana yine iletilmeli");
        await sessions.Received(1).AppendUserMessageAsync("s1", Query, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NonStreaming_WhenTheCallerCancels_NothingIsRecorded()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var (svc, sessions) = NewChatPort(new FakeTeam { RunAsyncThrows = new OperationCanceledException(cts.Token) });

        var act = () => svc.HandleAsync(new ChatRequest(Query, "s1"), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await sessions.DidNotReceive().AppendUserMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Voice_WhenTheTurnFailsWithNoResponse_TheTranscriptIsStillRecorded()
    {
        var sessions = Substitute.For<ISessionManager>();
        sessions.GetHistoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<ConversationMessage>());

        var guard = Substitute.For<IInputGuard>();
        guard.Inspect(Arg.Any<string>())
            .Returns(ci => new InputGuardResult(InputGuardVerdict.Allow, ci.Arg<string>() ?? "", [], null));

        var approvalContext = Substitute.For<IApprovalContextAccessor>();
        approvalContext.SetScope(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(Substitute.For<IDisposable>());

        var svc = new RealtimeBridgeService(
            Substitute.For<IRealtimeVoiceTransport>(),
            new FakeTeam { Events = [WorkflowError] },
            sessions,
            new NoopReasoning(),
            approvalContext,
            Substitute.For<IChatBridge>(),
            guard,
            new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 })),
            NullLogger<RealtimeBridgeService>.Instance);

        await svc.HandleUserTranscriptAsync(
            Substitute.For<IBrowserChannel>(), new AgentSession { SessionId = "s1" }, Query, CancellationToken.None);

        await sessions.Received(1).AppendUserMessageAsync("s1", Query, Arg.Any<CancellationToken>());
    }
}

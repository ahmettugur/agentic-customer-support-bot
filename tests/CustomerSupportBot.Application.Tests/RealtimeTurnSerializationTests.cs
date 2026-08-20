// Aynı oturumdaki sesli turların SIRAYA GİRMESİ.
//
// Sesli köprüde "tur işleniyor" bilgisi bağlantı kapsamlı bir bayraktı ve yalnızca kendi
// bağlantısını koruyordu. Aynı oturuma ikinci bir WebSocket açıldığında — ya da kullanıcı aynı
// anda metin sohbetini kullandığında — iki tur paralel çalışıyordu: aynı geçmiş üzerinde iki
// workflow, aynı state üzerinde iki yazma, aynı oturumda iki HITL akışı.
//
// Düzeltme, metin sohbetiyle AYNI oturum turu kilidini kullanmak. Aynı anahtar olduğu için
// ses ve metin birbirine göre de sıraya girer.

using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Tests.Shared;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Locking;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Application.Services.Realtime;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class RealtimeTurnSerializationTests
{
    private const string SessionId = "s-realtime-lock";

    /// <summary>Kaç turun AYNI ANDA pipeline'da olduğunu ölçen ekip.</summary>
    private sealed class ConcurrencyTrackingTeam : IAgentTeamPort
    {
        private int _active;
        public int MaxConcurrent;
        public readonly TaskCompletionSource FirstEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(
            string query, List<ConversationMessage>? history = null, AgentSession? session = null,
            ReasoningResult? reasoning = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var now = Interlocked.Increment(ref _active);
            InterlockedMax(ref MaxConcurrent, now);

            FirstEntered.TrySetResult();
            await Release.Task.ConfigureAwait(false);

            Interlocked.Decrement(ref _active);
            yield return new StreamEvent(
                StreamEventTypes.ResponseComplete, new ResponseCompletePayload("tamam"));
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int seen;
            while (value > (seen = Volatile.Read(ref target)))
                if (Interlocked.CompareExchange(ref target, value, seen) == seen) return;
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
        { yield break; await Task.CompletedTask; }
    }

    /// <summary>
    /// İki ayrı bağlantı = iki ayrı servis örneği. Kilit paylaşılır, çünkü gerçek kurulumda
    /// Redis üzerinden tüm bağlantılar ve pod'lar aynı kilidi görür.
    /// </summary>
    private static RealtimeBridgeService NewConnection(
        IAgentTeamPort team, IAppDistributedLock sharedLock)
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

        return new RealtimeBridgeService(
            Substitute.For<IRealtimeVoiceTransport>(),
            team, sessions, new NoopReasoning(), approvalContext,
            Substitute.For<IChatBridge>(), guard, sharedLock,
            NullLogger<RealtimeBridgeService>.Instance);
    }

    /// <summary>
    /// ASIL BULGU. İki bağlantı aynı oturum için aynı anda tur başlatır; ikisi aynı anda
    /// pipeline'da OLMAMALIDIR.
    /// </summary>
    [Fact]
    public async Task TwoConnectionsOnTheSameSession_DoNotRunTurnsConcurrently()
    {
        var ct = TestContext.Current.CancellationToken;
        var team = new ConcurrencyTrackingTeam();
        var sharedLock = new InMemoryDistributedLock(
            Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 30 }));

        var first = NewConnection(team, sharedLock).HandleUserTranscriptAsync(
            Substitute.For<IBrowserChannel>(),
            new AgentSession { SessionId = SessionId }, "birinci", ct);

        // Birinci tur pipeline'a girene kadar bekle — yarış deterministik kurulur.
        await team.FirstEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);

        var second = NewConnection(team, sharedLock).HandleUserTranscriptAsync(
            Substitute.For<IBrowserChannel>(),
            new AgentSession { SessionId = SessionId }, "ikinci", ct);

        // İkinci tur kilitte beklemeli; serbest bırakınca ikisi de tamamlanır.
        team.Release.TrySetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(60), ct);

        team.MaxConcurrent.Should().Be(1,
            "aynı oturumda iki tur aynı anda pipeline'da olmamalı — aynı geçmiş ve state üzerinde çalışıyorlar");
    }

    /// <summary>
    /// Karşı yön: FARKLI oturumlar birbirini beklememeli. Kilit oturum bazlı olmalı,
    /// yoksa tek bir yavaş tur tüm sesli trafiği sıraya sokar.
    /// </summary>
    [Fact]
    public async Task DifferentSessions_AreNotBlockedByEachOther()
    {
        var ct = TestContext.Current.CancellationToken;
        var team = new ConcurrencyTrackingTeam();
        var sharedLock = new InMemoryDistributedLock(
            Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 30 }));

        var a = NewConnection(team, sharedLock).HandleUserTranscriptAsync(
            Substitute.For<IBrowserChannel>(), new AgentSession { SessionId = "sess-A" }, "a", ct);
        await team.FirstEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);

        var b = NewConnection(team, sharedLock).HandleUserTranscriptAsync(
            Substitute.For<IBrowserChannel>(), new AgentSession { SessionId = "sess-B" }, "b", ct);

        team.Release.TrySetResult();
        await Task.WhenAll(a, b).WaitAsync(TimeSpan.FromSeconds(60), ct);

        team.MaxConcurrent.Should().Be(2, "farklı oturumlar paralel çalışabilmeli");
    }
}

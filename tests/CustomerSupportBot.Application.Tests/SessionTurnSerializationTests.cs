// Aynı oturumdaki turların SIRAYA girmesi.
//
// Bir tur "geçmişi oku → akıl yürüt → workflow → geçmişe yaz" dizisidir ve bu dizi atomik
// değildi: aynı anda gelen iki mesaj AYNI eski geçmişi okuyup sonuçlarını bitiş sırasına göre
// yazabiliyordu. Sonuç nedensel bağlamın kaybıdır — ikinci mesaj birincinin yanıtını görmez.

using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class SessionTurnSerializationTests
{
    /// <summary>Turun içinde ölçülebilir bir süre geçiren takım — örtüşme burada görünür.</summary>
    private sealed class OverlapDetectingTeam : IAgentTeamPort
    {
        private int _active;
        public int MaxConcurrent { get; private set; }

        public async Task<string> RunAsync(string query, List<ConversationMessage>? history = null,
            AgentSession? session = null, ReasoningResult? reasoning = null, CancellationToken ct = default)
        {
            var now = Interlocked.Increment(ref _active);
            MaxConcurrent = Math.Max(MaxConcurrent, now);
            try
            {
                await Task.Delay(60, ct);
                return "yanıt";
            }
            finally { Interlocked.Decrement(ref _active); }
        }

        public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(
            string query, List<ConversationMessage>? history = null, AgentSession? session = null,
            ReasoningResult? reasoning = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new StreamEvent(StreamEventTypes.ResponseComplete, null);
            await Task.CompletedTask;
        }

        public string GetWorkflowDiagram() => "";
    }

    private sealed class NoopReasoning : IReasoningPort
    {
        public Task<ReasoningResult> ReasonAsync(string query, AgentSession? session = null,
            List<ConversationMessage>? history = null, CancellationToken ct = default)
            => Task.FromResult(new ReasoningResult());

        public async IAsyncEnumerable<StreamEvent> ReasonStreamingAsync(
            string query, AgentSession session, List<ConversationMessage>? history = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new StreamEvent(StreamEventTypes.ReasoningComplete, new ReasoningResult());
            await Task.CompletedTask;
        }
    }

    private static ChatPortService Build(OverlapDetectingTeam team)
    {
        var session = new AgentSession { SessionId = "s1" };
        var sessions = Substitute.For<ISessionManager>();
        sessions.GetOrCreateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(session);
        sessions.GetHistoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => new List<ConversationMessage>());

        return new ChatPortService(
            team,
            new NoopReasoning(),
            sessions,
            Substitute.For<IChatModeRegistry>(),
            Substitute.For<IChatBridge>(),
            new SessionStateService(sessions, NullLogger<SessionStateService>.Instance),
            Substitute.For<IApprovalContextAccessor>(),
            new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 30 })),
            NullLogger<ChatPortService>.Instance);
    }

    [Fact]
    public async Task ConcurrentTurns_OnSameSession_DoNotOverlap()
    {
        var team = new OverlapDetectingTeam();
        var svc = Build(team);

        await Task.WhenAll(
            svc.HandleAsync(new ChatRequest("birinci", "s1"), TestContext.Current.CancellationToken),
            svc.HandleAsync(new ChatRequest("ikinci", "s1"), TestContext.Current.CancellationToken));

        team.MaxConcurrent.Should().Be(1,
            "aynı oturumun turları sıraya girmeli; örtüşürlerse ikisi de aynı eski geçmişi okur");
    }
}

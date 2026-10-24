// ChatPortService entegrasyonu: düşük güvenli turlar chip önerir, ardışıkta eskalasyon açar.

using CustomerSupportBot.Adapters.Persistence.InMemory;
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

public class LowConfidenceHandoffChatTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Team : IAgentTeamPort
    {
        public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(string query, List<ConversationMessage>? history = null,
            AgentSession? session = null, ReasoningResult? reasoning = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new StreamEvent(StreamEventTypes.ResponseComplete,
                new ResponseCompletePayload("Anlayamadım.", WellKnown.Termination.ReasonCompleted));
            await Task.CompletedTask;
        }
        public Task<string> RunAsync(string q, List<ConversationMessage>? h = null, AgentSession? s = null, ReasoningResult? r = null,
            CancellationToken ct = default) => Task.FromResult("");
        public string GetWorkflowDiagram() => "";
    }

    private sealed class Reasoning(double confidence, bool clarify = false) : IReasoningPort
    {
        public Task<ReasoningResult> ReasonAsync(string q, AgentSession? s = null, List<ConversationMessage>? h = null,
            CancellationToken ct = default) => Task.FromResult(new ReasoningResult());

        public async IAsyncEnumerable<StreamEvent> ReasonStreamingAsync(string q, AgentSession? s = null,
            List<ConversationMessage>? h = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new StreamEvent(StreamEventTypes.ReasoningComplete,
                clarify
                    ? new ReasoningResult { Intent = WellKnown.Intents.General, ConfidenceScore = confidence, Steps = [new ReasoningStep { Order = 1, Action = "clarify", Description = "netleştir" }] }
                    : new ReasoningResult { Intent = WellKnown.Intents.OrderInquiry, ConfidenceScore = confidence });
            await Task.CompletedTask;
        }
    }

    private static (ChatPortService Chat, IEscalationSink Esc, InMemorySessionManager Sessions) Build(
        double confidence, LowConfidenceHandoffOptions? options = null, bool clarify = false)
    {
        var sessions = new InMemorySessionManager(new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 5 })));
        var esc = Substitute.For<IEscalationSink>();
        esc.GetOpen().Returns([]);
        esc.CreateAsync(Arg.Any<EscalationRequest>()).Returns(ci => ci.Arg<EscalationRequest>());
        var modes = Substitute.For<IChatModeRegistry>();
        modes.GetMode(Arg.Any<string>()).Returns(ChatMode.Bot);
        var chat = new ChatPortService(new Team(), new Reasoning(confidence, clarify), sessions, modes, Substitute.For<IChatBridge>(),
            new SessionStateService(sessions, NullLogger<SessionStateService>.Instance), Substitute.For<IApprovalContextAccessor>(),
            new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 5 })),
            NullLogger<ChatPortService>.Instance, escalations: esc,
            handoff: Options.Create(options ?? new LowConfidenceHandoffOptions()));
        return (chat, esc, sessions);
    }

    private static async Task<List<StreamEvent>> TurnAsync(ChatPortService chat, string sessionId, string text)
    {
        var events = new List<StreamEvent>();
        await foreach (var e in chat.HandleStreamAsync(new ChatRequest(text, sessionId), Ct)) events.Add(e);
        return events;
    }

    private static string SessionIdOf(List<StreamEvent> events) =>
        ((SessionEventPayload)events.First(e => e.Type == StreamEventTypes.Session).Data!).SessionId;

    private static IEnumerable<string> Chips(List<StreamEvent> events) =>
        events.Where(e => e.Type == StreamEventTypes.UiHint)
            .Select(e => System.Text.Json.JsonSerializer.Serialize(e.Data, new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            .Where(j => j.Contains("suggested_replies"));

    [Fact]
    public async Task OneLowConfidenceTurn_OffersTheHumanChip_WithoutEscalating()
    {
        var (chat, esc, _) = Build(0.3);

        var events = await TurnAsync(chat, "s-low-1", "bi şey");

        Chips(events).Should().ContainSingle().Which.Should().Contain("Temsilciyle görüşmek istiyorum");
        await esc.DidNotReceive().CreateAsync(Arg.Any<EscalationRequest>());
    }

    [Fact]
    public async Task TwoConsecutiveLowConfidenceTurns_OpenAnEscalation_WithAUsableReason()
    {
        var (chat, esc, _) = Build(0.3);
        var first = await TurnAsync(chat, "s-low-2", "bi şey");
        var sid = SessionIdOf(first);

        await TurnAsync(chat, sid, "yine bi şey");

        await esc.Received(1).CreateAsync(Arg.Is<EscalationRequest>(r =>
            r.SessionId == sid && r.AgentName == "LowConfidence" && r.UserQuery == "yine bi şey"
            && r.Reason.Contains("netleştiremedim")));
    }

    [Fact]
    public async Task AConfidentTurn_BetweenLowOnes_ResetsTheStreak()
    {
        var (chatLow, esc, sessions) = Build(0.3);
        var sid = SessionIdOf(await TurnAsync(chatLow, "s-low-3", "bi şey"));
        var s = await sessions.GetAsync(sid, Ct);
        s!.State.LowConfidenceTurns = 0; // güvenli bir tur araya girmiş gibi

        await TurnAsync(chatLow, sid, "yine");

        await esc.DidNotReceive().CreateAsync(Arg.Any<EscalationRequest>());
    }

    [Fact]
    public async Task ConfidentTurns_NeverOfferOrEscalate()
    {
        var (chat, esc, _) = Build(0.9);

        var events = await TurnAsync(chat, "s-hi", "siparişim nerede");

        Chips(events).Should().NotContain(j => j.Contains("Temsilciyle görüşmek istiyorum"));
        await esc.DidNotReceive().CreateAsync(Arg.Any<EscalationRequest>());
    }

    [Fact]
    public async Task ARepeatedClarificationLoop_Escalates_EvenWhenTheModelStatesHighConfidence()
    {
        // Canlı veri: belirsiz mesajlarda bile model 0,8+ güven bildiriyor; sinyal "genel niyet + yalnızca netleştirme".
        var (chat, esc, _) = Build(0.86, clarify: true);
        var sid = SessionIdOf(await TurnAsync(chat, "s-clarify", "mmm şey işte o"));

        await TurnAsync(chat, sid, "olmadı yine o mevzu");

        await esc.Received(1).CreateAsync(Arg.Is<EscalationRequest>(r => r.SessionId == sid && r.AgentName == "LowConfidence"));
    }
}

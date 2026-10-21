// Önerilerin akışa girişi: tur bitince ui_hint "suggested_replies" olarak, yanıt ve kayıttan SONRA yayınlanır.

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

public class SuggestedRepliesStreamTests
{
    private sealed class FakeTeam(params StreamEvent[] events) : IAgentTeamPort
    {
        public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(string query, List<ConversationMessage>? history = null,
            AgentSession? session = null, ReasoningResult? reasoning = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var e in events) yield return e;
            await Task.CompletedTask;
        }

        public Task<string> RunAsync(string query, List<ConversationMessage>? history = null, AgentSession? session = null,
            ReasoningResult? reasoning = null, CancellationToken ct = default) => Task.FromResult("");

        public string GetWorkflowDiagram() => "";
    }

    private sealed class FakeReasoning(ReasoningResult result) : IReasoningPort
    {
        public Task<ReasoningResult> ReasonAsync(string query, AgentSession? session = null, List<ConversationMessage>? history = null,
            CancellationToken ct = default) => Task.FromResult(result);

        public async IAsyncEnumerable<StreamEvent> ReasonStreamingAsync(string query, AgentSession? session = null,
            List<ConversationMessage>? history = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new StreamEvent(StreamEventTypes.ReasoningComplete, result);
            await Task.CompletedTask;
        }
    }

    private static async Task<List<StreamEvent>> RunAsync(ReasoningResult reasoning, params StreamEvent[] teamEvents)
    {
        var session = new AgentSession { SessionId = "s1" };
        var sessions = Substitute.For<ISessionManager>();
        sessions.GetOrCreateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(session);
        sessions.ReloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(session);
        sessions.GetHistoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new List<ConversationMessage>());
        var modes = Substitute.For<IChatModeRegistry>();
        modes.GetMode(Arg.Any<string>()).Returns(ChatMode.Bot);

        var svc = new ChatPortService(new FakeTeam(teamEvents), new FakeReasoning(reasoning), sessions, modes, Substitute.For<IChatBridge>(),
            new SessionStateService(sessions, NullLogger<SessionStateService>.Instance), Substitute.For<IApprovalContextAccessor>(),
            new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 5 })), NullLogger<ChatPortService>.Instance);

        var events = new List<StreamEvent>();
        await foreach (var e in svc.HandleStreamAsync(new ChatRequest("siparişim nerede", "s1"), TestContext.Current.CancellationToken))
            events.Add(e);
        return events;
    }

    private static StreamEvent Complete(string text, string reason = WellKnown.Termination.ReasonCompleted) =>
        new(StreamEventTypes.ResponseComplete, new ResponseCompletePayload(text, TerminationReason: reason));

    private static readonly System.Text.Json.JsonSerializerOptions Relaxed =
        new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static StreamEvent? Suggestions(List<StreamEvent> events) =>
        events.FirstOrDefault(e => e.Type == StreamEventTypes.UiHint
            && System.Text.Json.JsonSerializer.Serialize(e.Data).Contains("suggested_replies"));

    [Fact]
    public async Task ACompletedOrderLookup_EmitsSuggestions_AfterTheAnswer()
    {
        var events = await RunAsync(new ReasoningResult { Intent = WellKnown.Intents.OrderInquiry }, Complete("Siparişiniz kargoda."));

        var hint = Suggestions(events);
        hint.Should().NotBeNull();
        System.Text.Json.JsonSerializer.Serialize(hint!.Data, Relaxed).Should().Contain("Siparişimi iptal etmek istiyorum");
        events.IndexOf(hint).Should().BeGreaterThan(events.FindIndex(e => e.Type == StreamEventTypes.ResponseComplete),
            "öneriler yanıttan SONRA gelir");
    }

    [Fact]
    public async Task AQuestionBackToTheCustomer_EmitsNoSuggestions()
    {
        var events = await RunAsync(new ReasoningResult { Intent = WellKnown.Intents.OrderInquiry }, Complete("Hangi sipariş?"));

        Suggestions(events).Should().BeNull();
    }

    [Fact]
    public async Task AFailedTurn_EmitsNoSuggestions()
    {
        var events = await RunAsync(new ReasoningResult { Intent = WellKnown.Intents.OrderInquiry },
            Complete("Bir sorun oluştu.", WellKnown.Termination.ReasonError));

        Suggestions(events).Should().BeNull();
    }

    [Fact]
    public async Task ACompoundTurn_EmitsNoSuggestions()
    {
        var reasoning = new ReasoningResult
        {
            Intent = WellKnown.Intents.OrderInquiry,
            SubTasks = [new() { Order = 1, Description = "a", TargetAgent = "OrderAgent" }, new() { Order = 2, Description = "b", TargetAgent = "ProductAgent" }]
        };

        Suggestions(await RunAsync(reasoning, Complete("Tamam."))).Should().BeNull();
    }

    [Fact]
    public async Task WithoutAResponseComplete_EmitsNoSuggestions()
    {
        var events = await RunAsync(new ReasoningResult { Intent = WellKnown.Intents.OrderInquiry },
            new StreamEvent(StreamEventTypes.Error, new { message = "hata" }));

        Suggestions(events).Should().BeNull();
    }
}

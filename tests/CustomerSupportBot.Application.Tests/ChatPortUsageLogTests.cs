// Tur bitince Reasoning + ajanların token harcaması tek [Usage] satırı olarak loglanır — hem akışta hem akışsız yolda.

using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Application.Services.Telemetry;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class ChatPortUsageLogTests
{
    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }

    private sealed class SpendingTeam(TurnUsageTracker tracker) : IAgentTeamPort
    {
        public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(
            string query, List<ConversationMessage>? history = null, AgentSession? session = null, ReasoningResult? reasoning = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            tracker.Record("ProductAgent", 900, 60);
            yield return new StreamEvent(StreamEventTypes.ResponseDelta, new TextDeltaPayload("a"));
            await Task.Yield();
            tracker.Record("ResponseAgent", 1200, 150);
            yield return new StreamEvent(StreamEventTypes.ResponseComplete, new ResponseCompletePayload("a"));
        }

        public Task<string> RunAsync(string query, List<ConversationMessage>? history = null, AgentSession? session = null,
            ReasoningResult? reasoning = null, CancellationToken ct = default)
        {
            tracker.Record("ProductAgent", 900, 60);
            return Task.FromResult("ok");
        }

        public string GetWorkflowDiagram() => "";
    }

    private sealed class SpendingReasoning(TurnUsageTracker tracker) : IReasoningPort
    {
        public Task<ReasoningResult> ReasonAsync(string query, AgentSession? session = null, List<ConversationMessage>? history = null, CancellationToken ct = default)
        {
            tracker.Record("Reasoning", 400, 80);
            return Task.FromResult(new ReasoningResult());
        }

        public async IAsyncEnumerable<StreamEvent> ReasonStreamingAsync(string query, AgentSession? session = null, List<ConversationMessage>? history = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            tracker.Record("Reasoning", 400, 80);
            await Task.Yield();
            yield break;
        }
    }

    private static (ChatPortService Service, ListLogger<ChatPortService> Log) Build()
    {
        var attribution = new LlmCallAttribution();
        var tracker = new TurnUsageTracker(attribution);
        var session = new AgentSession { SessionId = "session-abc-123" };
        var sessions = Substitute.For<ISessionManager>();
        sessions.GetOrCreateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(session);
        sessions.ReloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(session);
        sessions.GetHistoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new List<ConversationMessage>());
        var modes = Substitute.For<IChatModeRegistry>();
        modes.GetMode(Arg.Any<string>()).Returns(ChatMode.Bot);
        var log = new ListLogger<ChatPortService>();
        var service = new ChatPortService(new SpendingTeam(tracker), new SpendingReasoning(tracker), sessions, modes, Substitute.For<IChatBridge>(),
            new SessionStateService(sessions, Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionStateService>.Instance),
            Substitute.For<IApprovalContextAccessor>(),
            new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 5 })),
            log, attribution: attribution, usage: tracker);
        return (service, log);
    }

    [Fact]
    public async Task Stream_LogsOneUsageLineWithEveryStep()
    {
        var (service, log) = Build();

        await foreach (var _ in service.HandleStreamAsync(new ChatRequest("merhaba", "session-abc-123"), TestContext.Current.CancellationToken)) { }

        var line = log.Messages.Should().ContainSingle(m => m.StartsWith("[Usage]")).Subject;
        line.Should().Contain("Reasoning").And.Contain("ProductAgent").And.Contain("ResponseAgent");
        line.Should().Contain("toplam in 2500/out 290");
        line.Should().NotContain("merhaba");
    }

    [Fact]
    public async Task NonStream_LogsOneUsageLine()
    {
        var (service, log) = Build();

        await service.HandleAsync(new ChatRequest("merhaba", "session-abc-123"), TestContext.Current.CancellationToken);

        var line = log.Messages.Should().ContainSingle(m => m.StartsWith("[Usage]")).Subject;
        line.Should().Contain("Reasoning").And.Contain("ProductAgent");
    }
}

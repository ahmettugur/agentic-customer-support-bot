// Görüşme başına maliyet: sohbet turundaki tüm LLM çağrıları (akıl yürütme + ajan takımı) o görüşmeye
// atfedilmeli — akış (stream) yolunda her yield'den sonra da.

using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Application.Services.Telemetry;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class ChatPortCostAttributionTests
{
    private sealed class CapturingTeam(LlmCallAttribution attribution) : IAgentTeamPort
    {
        public List<string?> Seen { get; } = [];

        public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(
            string query, List<ConversationMessage>? history = null, AgentSession? session = null,
            ReasoningResult? reasoning = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            Seen.Add(attribution.CurrentSessionId);
            yield return new StreamEvent(StreamEventTypes.ResponseDelta, new TextDeltaPayload("a"));
            await Task.Yield();
            Seen.Add(attribution.CurrentSessionId);   // tüketici bir sonraki öğeyi isteyince
            yield return new StreamEvent(StreamEventTypes.ResponseComplete, new ResponseCompletePayload("a"));
        }

        public Task<string> RunAsync(string query, List<ConversationMessage>? history = null,
            AgentSession? session = null, ReasoningResult? reasoning = null, CancellationToken ct = default)
        {
            Seen.Add(attribution.CurrentSessionId);
            return Task.FromResult("ok");
        }

        public string GetWorkflowDiagram() => "";
    }

    private sealed class CapturingReasoning(LlmCallAttribution attribution) : IReasoningPort
    {
        public List<string?> Seen { get; } = [];

        public Task<ReasoningResult> ReasonAsync(string query, AgentSession? session = null,
            List<ConversationMessage>? history = null, CancellationToken ct = default)
        {
            Seen.Add(attribution.CurrentSessionId);
            return Task.FromResult(new ReasoningResult());
        }

        public async IAsyncEnumerable<StreamEvent> ReasonStreamingAsync(
            string query, AgentSession? session = null, List<ConversationMessage>? history = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            Seen.Add(attribution.CurrentSessionId);
            await Task.Yield();
            yield break;
        }
    }

    private static (ChatPortService Service, CapturingTeam Team, CapturingReasoning Reasoning, LlmCallAttribution Attribution) Build()
    {
        var attribution = new LlmCallAttribution();
        var session = new AgentSession { SessionId = "s1" };
        var sessions = Substitute.For<ISessionManager>();
        sessions.GetOrCreateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(session);
        sessions.ReloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(session);
        sessions.GetHistoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new List<ConversationMessage>());
        var modes = Substitute.For<IChatModeRegistry>();
        modes.GetMode(Arg.Any<string>()).Returns(ChatMode.Bot);
        var team = new CapturingTeam(attribution);
        var reasoning = new CapturingReasoning(attribution);
        var service = new ChatPortService(team, reasoning, sessions, modes, Substitute.For<IChatBridge>(),
            new SessionStateService(sessions, NullLogger<SessionStateService>.Instance),
            Substitute.For<IApprovalContextAccessor>(),
            new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 5 })),
            NullLogger<ChatPortService>.Instance, attribution: attribution);
        return (service, team, reasoning, attribution);
    }

    [Fact]
    public async Task Stream_EveryLlmStepIsAttributedToTheConversation()
    {
        var (service, team, reasoning, attribution) = Build();

        await foreach (var _ in service.HandleStreamAsync(new ChatRequest("merhaba", "s1"))) { }

        reasoning.Seen.Should().Equal("s1");
        team.Seen.Should().Equal("s1", "s1");
        attribution.CurrentSessionId.Should().BeNull("kapsam tur bitince kapanır");
    }

    [Fact]
    public async Task NonStream_EveryLlmStepIsAttributedToTheConversation()
    {
        var (service, team, reasoning, _) = Build();

        await service.HandleAsync(new ChatRequest("merhaba", "s1"));

        reasoning.Seen.Should().Equal("s1");
        team.Seen.Should().Equal("s1");
    }
}

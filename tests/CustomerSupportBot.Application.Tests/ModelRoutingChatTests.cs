// ChatPortService, takım çalışırken model katmanını ortama koyar (akışlı yolda her MoveNext'te yeniden).

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class ModelRoutingChatTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class SpyTeam(ITurnModelTier tier) : IAgentTeamPort
    {
        public List<ModelTier> Seen { get; } = [];

        public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(string query, List<ConversationMessage>? history = null,
            AgentSession? session = null, ReasoningResult? reasoning = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            Seen.Add(tier.Current);
            yield return new StreamEvent(StreamEventTypes.ResponseDelta, new TextDeltaPayload("a"));
            await Task.Yield();
            Seen.Add(tier.Current);                    // await sonrası da aynı katman
            yield return new StreamEvent(StreamEventTypes.ResponseComplete, new ResponseCompletePayload("a", WellKnown.Termination.ReasonCompleted));
        }

        public Task<string> RunAsync(string q, List<ConversationMessage>? h = null, AgentSession? s = null, ReasoningResult? r = null,
            CancellationToken ct = default) { Seen.Add(tier.Current); return Task.FromResult("a"); }

        public string GetWorkflowDiagram() => "";
    }

    private sealed class Reasoning(ReasoningResult result) : IReasoningPort
    {
        public Task<ReasoningResult> ReasonAsync(string q, AgentSession? s = null, List<ConversationMessage>? h = null,
            CancellationToken ct = default) => Task.FromResult(result);

        public async IAsyncEnumerable<StreamEvent> ReasonStreamingAsync(string q, AgentSession? s = null, List<ConversationMessage>? h = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new StreamEvent(StreamEventTypes.ReasoningComplete, result);
            await Task.CompletedTask;
        }
    }

    private static (ChatPortService Chat, SpyTeam Team, ITurnModelTier Tier) Build(ReasoningResult reasoning, bool enabled = true)
    {
        var tier = new AsyncLocalTurnModelTier();
        var team = new SpyTeam(tier);
        var sessions = new InMemorySessionManager(new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 5 })));
        var modes = Substitute.For<IChatModeRegistry>();
        modes.GetMode(Arg.Any<string>()).Returns(ChatMode.Bot);
        var chat = new ChatPortService(team, new Reasoning(reasoning), sessions, modes, Substitute.For<IChatBridge>(),
            new SessionStateService(sessions, NullLogger<SessionStateService>.Instance), Substitute.For<IApprovalContextAccessor>(),
            new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 5 })),
            NullLogger<ChatPortService>.Instance,
            modelTier: tier, routing: Options.Create(new ModelRoutingOptions { Enabled = enabled }));
        return (chat, team, tier);
    }

    private static ReasoningResult Simple => new() { Intent = WellKnown.Intents.ProductInfo, ConfidenceScore = 0.9 };

    [Fact]
    public async Task StreamingTurn_Simple_RunsTheWholeTeamOnTheFastTier()
    {
        var (chat, team, tier) = Build(Simple);

        await foreach (var _ in chat.HandleStreamAsync(new ChatRequest("ürünler", "s-fast"), Ct)) { }

        team.Seen.Should().Equal(ModelTier.Fast, ModelTier.Fast);
        tier.Current.Should().Be(ModelTier.Standard, "kapsam tur bitince geri alınır");
    }

    [Fact]
    public async Task StreamingTurn_Complex_StaysStandard()
    {
        var (chat, team, _) = Build(new ReasoningResult { Intent = WellKnown.Intents.Complaint, ConfidenceScore = 0.95 });

        await foreach (var _ in chat.HandleStreamAsync(new ChatRequest("şikayet", "s-std"), Ct)) { }

        team.Seen.Should().OnlyContain(t => t == ModelTier.Standard);
    }

    [Fact]
    public async Task NonStreamingTurn_UsesTheSamePolicy()
    {
        var (chat, team, _) = Build(Simple);

        await chat.HandleAsync(new ChatRequest("ürünler", "s-ns"), Ct);

        team.Seen.Should().Equal(ModelTier.Fast);
    }

    [Fact]
    public async Task WhenRoutingIsDisabled_EverythingIsStandard()
    {
        var (chat, team, _) = Build(Simple, enabled: false);

        await foreach (var _ in chat.HandleStreamAsync(new ChatRequest("ürünler", "s-off"), Ct)) { }

        team.Seen.Should().OnlyContain(t => t == ModelTier.Standard);
    }
}

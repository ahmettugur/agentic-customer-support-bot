// Canlı değerlendirme HER sohbet yolundan beslenir: akışlı (web sohbeti) ve akışsız (HandleAsync; MCP/A2A/e-posta kanalı).
// Canlı denemede yalnızca akışlı yol bağlıydı — e-posta/MCP üzerinden gelen yanıtlar hiç puanlanmıyordu.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Application.Services.Evaluation;
using CustomerSupportBot.Application.Services.Memory;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class LiveEvaluationChatTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Judge : IGeneralChatClient
    {
        public int Calls;
        public Task<string> CompleteAsync(IReadOnlyList<ConversationMessage> messages, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult("""{"helpfulness":4,"groundedness":4,"tone":4}""");
        }
    }

    private sealed class Team : IAgentTeamPort
    {
        public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(string q, List<ConversationMessage>? h = null, AgentSession? s = null,
            ReasoningResult? r = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new StreamEvent(StreamEventTypes.ResponseComplete, new ResponseCompletePayload("Siparişiniz yolda.", WellKnown.Termination.ReasonCompleted));
            await Task.CompletedTask;
        }
        public Task<string> RunAsync(string q, List<ConversationMessage>? h = null, AgentSession? s = null, ReasoningResult? r = null,
            CancellationToken ct = default) => Task.FromResult("Siparişiniz yolda.");
        public string GetWorkflowDiagram() => "";
    }

    private sealed class Reasoning : IReasoningPort
    {
        public Task<ReasoningResult> ReasonAsync(string q, AgentSession? s = null, List<ConversationMessage>? h = null, CancellationToken ct = default)
            => Task.FromResult(new ReasoningResult());
        public async IAsyncEnumerable<StreamEvent> ReasonStreamingAsync(string q, AgentSession? s = null, List<ConversationMessage>? h = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield break;
        }
    }

    private static (ChatPortService Chat, Judge Judge) Build()
    {
        var judge = new Judge();
        var prompts = Substitute.For<IPromptRepository>();
        prompts.Get(LiveEvaluationService.PromptKey).Returns("hakem");
        prompts.SetVersion.Returns("ps-1");
        var live = new LiveEvaluationService(judge, prompts, new ContextSanitizer(),
            Options.Create(new LiveEvaluationOptions { Enabled = true, SampleRate = 1 }), NullLogger<LiveEvaluationService>.Instance);
        var sessions = new InMemorySessionManager(new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 5 })));
        var modes = Substitute.For<IChatModeRegistry>();
        modes.GetMode(Arg.Any<string>()).Returns(ChatMode.Bot);
        var chat = new ChatPortService(new Team(), new Reasoning(), sessions, modes, Substitute.For<IChatBridge>(),
            new SessionStateService(sessions, NullLogger<SessionStateService>.Instance), Substitute.For<IApprovalContextAccessor>(),
            new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 5 })),
            NullLogger<ChatPortService>.Instance, liveEval: live);
        return (chat, judge);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(20, Ct);
    }

    [Fact]
    public async Task TheStreamingTurn_IsSampled()
    {
        var (chat, judge) = Build();

        await foreach (var _ in chat.HandleStreamAsync(new ChatRequest("siparişim nerede", "s-live-1"), Ct)) { }
        await WaitForAsync(() => judge.Calls > 0);

        judge.Calls.Should().Be(1);
    }

    [Fact]
    public async Task TheNonStreamingTurn_IsSampledToo()
    {
        var (chat, judge) = Build();

        await chat.HandleAsync(new ChatRequest("siparişim nerede", "s-live-2"), Ct);
        await WaitForAsync(() => judge.Calls > 0);

        judge.Calls.Should().Be(1);
    }
}

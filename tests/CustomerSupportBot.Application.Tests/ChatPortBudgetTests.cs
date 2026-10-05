// Harcama limiti — sohbet turu başında ön kontrol: aşıldıysa LLM'e gidilmez, müşteri mesaj alır.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Budget;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class ChatPortBudgetTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(
        ChatPortService Service, IReasoningPort Reasoning, IAgentTeamPort Team, ILlmSpendGuard Guard,
        InMemorySessionManager Sessions, InMemoryEscalationSink Escalations);

    private static Harness Build(LlmBudgetExceeded? exceeded)
    {
        var sessions = new InMemorySessionManager(new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 5 })));
        var modes = Substitute.For<IChatModeRegistry>();
        modes.GetMode(Arg.Any<string>()).Returns(ChatMode.Bot);
        var reasoning = Substitute.For<IReasoningPort>();
        reasoning.ReasonAsync(default!, default!, default, default).ReturnsForAnyArgs(new ReasoningResult());
        var team = Substitute.For<IAgentTeamPort>();
        team.RunAsync(default!, default, default, default, default).ReturnsForAnyArgs("bot yanıtı");
        var guard = Substitute.For<ILlmSpendGuard>();
        guard.CheckAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(exceeded);
        var escalations = new InMemoryEscalationSink(NullLogger<InMemoryEscalationSink>.Instance);
        var service = new ChatPortService(team, reasoning, sessions, modes, Substitute.For<IChatBridge>(),
            new SessionStateService(sessions, NullLogger<SessionStateService>.Instance),
            Substitute.For<IApprovalContextAccessor>(),
            new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 5 })),
            NullLogger<ChatPortService>.Instance,
            spendGuard: guard, escalations: escalations, budgetOptions: Options.Create(new LlmBudgetOptions()));
        return new Harness(service, reasoning, team, guard, sessions, escalations);
    }

    [Fact]
    public async Task WithinBudget_RunsTheNormalTurn()
    {
        var h = Build(exceeded: null);

        (await h.Service.HandleAsync(new ChatRequest("merhaba", "b1"), Ct)).Response.Should().Be("bot yanıtı");

        await h.Guard.Received(1).CheckAsync("b1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DailyLimit_RepliesWithoutCallingTheModel_AndWithoutEscalating()
    {
        var h = Build(new LlmBudgetExceeded(LlmBudgetScope.Daily, 50m, 50.2m));

        var r = await h.Service.HandleAsync(new ChatRequest("siparişim nerede", "b2"), Ct);

        r.Response.Should().Be(new LlmBudgetOptions().UnavailableMessage);
        await h.Reasoning.DidNotReceiveWithAnyArgs().ReasonAsync(default!, default!, default, Ct);
        await h.Team.DidNotReceiveWithAnyArgs().RunAsync(default!, default, default, default, Ct);
        (await h.Sessions.GetHistoryAsync("b2", Ct)).Select(m => m.Text).Should().Equal("siparişim nerede", r.Response);
        h.Escalations.GetOpen().Should().BeEmpty();
    }

    [Fact]
    public async Task ConversationLimit_HandsTheConversationToAHuman_Once()
    {
        var h = Build(new LlmBudgetExceeded(LlmBudgetScope.Conversation, 1m, 1.1m));

        var first = await h.Service.HandleAsync(new ChatRequest("yine mi?", "b3"), Ct);
        await h.Service.HandleAsync(new ChatRequest("cevap ver", "b3"), Ct);

        first.Response.Should().Be(new LlmBudgetOptions().ConversationLimitMessage);
        var escalation = h.Escalations.GetOpen().Should().ContainSingle().Subject;
        escalation.SessionId.Should().Be("b3");
        escalation.UserQuery.Should().Be("yine mi?");
        escalation.Reason.Should().Contain("bütçe");
    }

    [Fact]
    public async Task Stream_OverBudget_EmitsTheMessageAsAResponse()
    {
        var h = Build(new LlmBudgetExceeded(LlmBudgetScope.Monthly, 1000m, 1000m));

        var events = new List<StreamEvent>();
        await foreach (var e in h.Service.HandleStreamAsync(new ChatRequest("merhaba", "b4"), Ct)) events.Add(e);

        var message = new LlmBudgetOptions().UnavailableMessage;
        events.Select(e => e.Type).Should().Equal(
            StreamEventTypes.Session, StreamEventTypes.ResponseStart, StreamEventTypes.ResponseDelta, StreamEventTypes.ResponseComplete);
        events[2].Data.Should().Be(new TextDeltaPayload(message));
        events[3].Data.Should().Be(new ResponseCompletePayload(message, TerminationReason: LlmBudgetOptions.TerminationReason));
        h.Reasoning.DidNotReceiveWithAnyArgs().ReasonStreamingAsync(default!, default!, default, Ct);
        (await h.Sessions.GetHistoryAsync("b4", Ct)).Select(m => m.Text).Should().Equal("merhaba", message);
    }
}

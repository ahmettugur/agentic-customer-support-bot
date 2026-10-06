// Yapay zekâ çözüm oranı (containment) ve görüşme başına maliyet.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Application.Services.Telemetry;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class ContainmentMetricsTests
{
    private sealed record Harness(
        AnalyticsPortService Analytics, HumanInvolvementTracker Tracker, InMemorySessionManager Sessions,
        InMemoryEscalationSink Escalations, ILlmCallPersistencePort Costs);

    private static Harness Build()
    {
        var sessions = new InMemorySessionManager(new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 })));
        var escalations = new InMemoryEscalationSink(NullLogger<InMemoryEscalationSink>.Instance);
        var approvals = new InMemoryApprovalQueue(Options.Create(new ApprovalOptions()), Substitute.For<IApprovalExecutionRouter>(),
            NullLogger<InMemoryApprovalQueue>.Instance);
        var costs = Substitute.For<ILlmCallPersistencePort>();
        costs.GetCostSummaryAsync(Arg.Any<DateTime?>(), Arg.Any<CancellationToken>()).Returns(LlmCostSummary.Empty);
        var analytics = new AnalyticsPortService(new InMemoryRatingStore(NullLogger<InMemoryRatingStore>.Instance), sessions,
            approvals, escalations, NullLogger<AnalyticsPortService>.Instance, costs);
        return new Harness(analytics, new HumanInvolvementTracker(sessions), sessions, escalations, costs);
    }

    private static async Task<string> ConversationAsync(Harness h, int turns = 1)
    {
        var s = await h.Sessions.GetOrCreateAsync(null);
        for (var i = 0; i < turns; i++) await h.Sessions.AddExchangeAsync(s.SessionId, $"soru {i}", "yanıt");
        return s.SessionId;
    }

    [Fact]
    public async Task ContainmentRate_CountsConversationsResolvedWithoutAHuman()
    {
        var h = Build();
        var botOnly1 = await ConversationAsync(h);
        var botOnly2 = await ConversationAsync(h, turns: 3);
        var takenOver = await ConversationAsync(h);
        var escalatedLegacy = await ConversationAsync(h);
        await ConversationAsync(h, turns: 0);   // boş görüşme hesaba girmez
        await h.Tracker.MarkAsync(takenOver, TestContext.Current.CancellationToken);
        await h.Escalations.CreateAsync(new EscalationRequest { SessionId = escalatedLegacy, UserQuery = "q", Reason = "r" });

        var d = await h.Analytics.GetDashboardAsync(TestContext.Current.CancellationToken);

        d.EligibleSessions.Should().Be(4);
        d.HumanInvolvedSessions.Should().Be(2, "devralınan + (bayrağı olmayan eski) eskalasyonlu görüşme");
        d.ContainedSessions.Should().Be(2);
        d.ContainmentRate.Should().Be(0.5);
        _ = (botOnly1, botOnly2);
    }

    [Fact]
    public async Task NoConversations_RateIsZero_NotNaN()
    {
        var d = await Build().Analytics.GetDashboardAsync(TestContext.Current.CancellationToken);

        d.EligibleSessions.Should().Be(0);
        d.ContainmentRate.Should().Be(0);
    }

    [Fact]
    public async Task CostPerConversation_ComesFromTheCostSummary()
    {
        var h = Build();
        h.Costs.GetCostSummaryAsync(Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(new LlmCostSummary(TotalCostUsd: 1.50m, AttributedCostUsd: 1.20m, SessionsWithCost: 4,
                AverageCostPerSessionUsd: 0.30m, MedianCostPerSessionUsd: 0.25m));

        var d = await h.Analytics.GetDashboardAsync(TestContext.Current.CancellationToken);

        d.TotalLlmCostUsd.Should().Be(1.50m);
        d.UnattributedLlmCostUsd.Should().Be(0.30m);
        d.SessionsWithCost.Should().Be(4);
        d.AverageCostPerConversationUsd.Should().Be(0.30m);
        d.MedianCostPerConversationUsd.Should().Be(0.25m);
    }

    [Fact]
    public async Task Tracker_MarksOnce_AndIgnoresUnknownSessions()
    {
        var h = Build();
        var sid = await ConversationAsync(h);

        await h.Tracker.MarkAsync(sid, TestContext.Current.CancellationToken);
        var first = (await h.Sessions.GetAsync(sid, TestContext.Current.CancellationToken))!.State.HumanInvolvedAt;
        await h.Tracker.MarkAsync(sid, TestContext.Current.CancellationToken);
        await h.Tracker.MarkAsync("olmayan", TestContext.Current.CancellationToken);
        await h.Tracker.MarkAsync(null, TestContext.Current.CancellationToken);

        var state = (await h.Sessions.GetAsync(sid, TestContext.Current.CancellationToken))!.State;
        state.HumanInvolved.Should().BeTrue();
        state.HumanInvolvedAt.Should().Be(first);
    }
}

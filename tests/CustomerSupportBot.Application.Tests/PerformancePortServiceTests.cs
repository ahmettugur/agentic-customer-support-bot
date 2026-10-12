// Performans raporu: aralık doğrulama, sonuç gruplama/oranlar, temsilcilerin birleştirilmesi.

using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Telemetry;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Tests;

public class PerformancePortServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

    private static (PerformancePortService Service, IPerformanceStore Store) Build(
        AiPerformanceData? ai = null, IReadOnlyList<HumanAgentStat>? human = null)
    {
        var store = Substitute.For<IPerformanceStore>();
        store.GetAiAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(ai ?? new AiPerformanceData(new TraceTotals(0, 0, 0, 0, 0), [], [], [], []));
        store.GetHumanAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(human ?? []);
        var agents = Substitute.For<IHumanAgentRegistry>();
        agents.GetAll().Returns(
        [
            new HumanAgent { Id = "agent-jdoe", DisplayName = "John Doe", IsActive = true },
            new HumanAgent { Id = "agent-jsmith", DisplayName = "Jane Smith", IsActive = true },
            new HumanAgent { Id = "agent-old", DisplayName = "Eski", IsActive = false }
        ]);
        return (new PerformancePortService(store, agents, new FakeTimeProvider(Now)), store);
    }

    [Fact]
    public async Task DefaultRange_IsLastSevenDays()
    {
        var (svc, store) = Build();

        var r = await svc.GetAsync(null, null, Ct);

        r.ToUtc.Should().Be(Now);
        r.FromUtc.Should().Be(Now.AddDays(-7));
        await store.Received(1).GetAiAsync(Now.AddDays(-7), Now, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(93)]
    public async Task InvalidRange_Throws(int days)
    {
        var (svc, _) = Build();

        var act = () => svc.GetAsync(Now.AddDays(-days), Now, Ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Agents_GroupOutcomes_ComputeSuccessRate_InCanonicalOrder()
    {
        var (svc, _) = Build(new AiPerformanceData(
            new TraceTotals(10, 8, 1, 2500, 2),
            [new AgentTurnStat("ResponseAgent", 9, 900), new AgentTurnStat("OrderAgent", 6, 3000), new AgentTurnStat("PlanningAgent", 10, 1200)],
            [
                new AgentOutcomeStat("OrderAgent", "done", 3), new AgentOutcomeStat("OrderAgent", "pending_approval", 1),
                new AgentOutcomeStat("OrderAgent", "needs_escalation", 1), new AgentOutcomeStat("OrderAgent", "needs_followup", 2),
                new AgentOutcomeStat("OrderAgent", "failed", 1), new AgentOutcomeStat("ComplaintAgent", "done", 2)
            ],
            [new ToolStat("order_status_tool", "OrderAgent", 8, 0, null), new ToolStat("order_cancel_tool", "OrderAgent", 2, 1, "X: yok")],
            [new TerminationCount("completed", 8)]));

        var r = await svc.GetAsync(null, null, Ct);

        r.Agents.Select(a => a.AgentName).Should().Equal("PlanningAgent", "OrderAgent", "ComplaintAgent", "ResponseAgent");
        var order = r.Agents.Single(a => a.AgentName == "OrderAgent");
        order.Should().Match<AiAgentRow>(a => a.Turns == 6 && a.Outcomes == 8 && a.Succeeded == 4 && a.Escalated == 1
                                             && a.NeedsFollowUp == 2 && a.Failed == 1);
        order.SuccessRate.Should().Be(0.5);
        r.Agents.Single(a => a.AgentName == "PlanningAgent").SuccessRate.Should().BeNull("planlama ajanı görev sonucu bildirmez");
        r.Agents.Single(a => a.AgentName == "ComplaintAgent").Turns.Should().Be(0);

        r.Summary.Should().Match<AiSummary>(s => s.Traces == 10 && s.CompletionRate == 0.8 && s.ErrorRate == 0.1
                                                && s.EscalationRate == 0.2 && s.ToolCalls == 10);
        r.Summary.ToolFailureRate.Should().Be(0.1);
        r.Tools.First().ToolName.Should().Be("order_cancel_tool", "en çok hata veren araç üstte");
        r.Tools.First().FailureRate.Should().Be(0.5);
    }

    [Fact]
    public async Task EmptyPeriod_HasNoDivisionByZero()
    {
        var (svc, _) = Build();

        var r = await svc.GetAsync(null, null, Ct);

        r.Summary.CompletionRate.Should().Be(0);
        r.Summary.ToolFailureRate.Should().BeNull();
        r.Agents.Should().BeEmpty();
    }

    [Fact]
    public async Task HumanAgents_IncludeIdleActiveAgents_WithDisplayNames()
    {
        var (svc, _) = Build(human:
        [
            new HumanAgentStat("agent-jsmith", 4, 3, 120, 1800, 2, 4.5, 2),
            new HumanAgentStat("agent-gone", 1, 1, 60, 600, 0, null, 0)
        ]);

        var r = await svc.GetAsync(null, null, Ct);

        r.HumanAgents.Select(h => h.DisplayName).Should().Equal("Jane Smith", "agent-gone", "John Doe");
        r.HumanAgents.Single(h => h.AgentId == "agent-jdoe").EscalationsTaken.Should().Be(0);
        r.HumanAgents.Should().NotContain(h => h.AgentId == "agent-old", "pasif ve işi olmayan temsilci listelenmez");
    }
}

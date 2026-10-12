// Performans toplama sorguları (gerçek Postgres, jsonb). Diğer testlerin verisine karışmasın diye 2031 penceresi.

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Analytics;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Catalog;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Hitl;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Observability;
using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Tests.Shared;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresPerformanceStoreTests(PostgresCatalogFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTime From = new(2031, 3, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = From.AddDays(7);

    private PostgresPerformanceStore Store => new(fixture.DbFactory);

    private static ReasoningTraceEntity Trace(DateTime at, string? termination, string? error, string visits, string reasonings, string tools) => new()
    {
        TraceId = Guid.NewGuid().ToString("N"),
        SessionId = "perf-" + Guid.NewGuid().ToString("N")[..8],
        UserQuery = "q",
        StartedAt = at,
        CompletedAt = termination is null ? null : at.AddSeconds(4),
        TerminationReason = termination,
        Error = error,
        AgentVisitsJson = visits,
        SpecialistReasoningsJson = reasonings,
        ToolCallsJson = tools
    };

    private const string Visits =
        """[{"AgentName":"PlanningAgent_aa","DurationMs":1000},{"AgentName":"OrderAgent_bb","DurationMs":3000}]""";

    [Fact]
    public async Task Ai_AggregatesTracesAgentsOutcomesAndTools_InsideTheWindowOnly()
    {
        await using (var ctx = fixture.DbFactory.CreateDbContext())
        {
            ctx.ReasoningTraces.AddRange(
                Trace(From.AddHours(1), "completed", null, Visits,
                    """[{"AgentName":"OrderAgent","PostToolReflection":{"Status":"done"}}]""",
                    """[{"ToolName":"order_status_tool","AgentName":"OrderAgent","Success":true},{"ToolName":"order_cancel_tool","AgentName":"OrderAgent","Success":false,"ResultSummary":"ORDER_NOT_FOUND: yok"}]"""),
                Trace(From.AddHours(2), "escalation_needed", null, """[{"AgentName":"OrderAgent_cc","DurationMs":5000}]""",
                    """[{"AgentName":"OrderAgent","PostToolReflection":{"Status":"needs_escalation"}}]""",
                    """[{"ToolName":"order_cancel_tool","AgentName":"OrderAgent","Success":false,"ResultSummary":"SYSTEM: zaman aşımı"}]"""),
                Trace(From.AddHours(3), null, "patladı", "[]", "[]", "[]"),
                // Pencere dışı — sayılmamalı.
                Trace(To.AddHours(1), "completed", null, Visits, "[]", """[{"ToolName":"order_status_tool","Success":false}]"""));
            await ctx.SaveChangesAsync(Ct);
        }

        var ai = await Store.GetAiAsync(From, To, Ct);

        ai.Totals.Traces.Should().Be(3);
        ai.Totals.Completed.Should().Be(2);
        ai.Totals.Errors.Should().Be(1);
        ai.Totals.Escalated.Should().Be(1);
        ai.Totals.AvgDurationMs.Should().BeApproximately(4000, 1);

        ai.Turns.Should().ContainSingle(t => t.AgentName == "OrderAgent").Which.Should().Match<Application.Ports.Outbound.Persistence.AgentTurnStat>(
            t => t.Turns == 2 && Math.Abs(t.AvgDurationMs - 4000) < 1);
        ai.Turns.Should().ContainSingle(t => t.AgentName == "PlanningAgent").Which.Turns.Should().Be(1);

        ai.Outcomes.Should().Contain(o => o.AgentName == "OrderAgent" && o.Status == "done" && o.Count == 1);
        ai.Outcomes.Should().Contain(o => o.AgentName == "OrderAgent" && o.Status == "needs_escalation" && o.Count == 1);

        var cancel = ai.Tools.Should().ContainSingle(t => t.ToolName == "order_cancel_tool").Subject;
        cancel.Calls.Should().Be(2);
        cancel.Failures.Should().Be(2);
        cancel.LastFailure.Should().Be("SYSTEM: zaman aşımı");
        ai.Tools.Should().ContainSingle(t => t.ToolName == "order_status_tool").Which.Failures.Should().Be(0);

        ai.Terminations.Should().Contain(t => t.Reason == "escalation_needed" && t.Count == 1);
        ai.Terminations.Should().Contain(t => t.Reason == "unknown" && t.Count == 1);
    }

    [Fact]
    public async Task Human_ComputesResponseAndResolutionTimes_ComplaintsAndRatings()
    {
        var agent = "perf-agent-" + Guid.NewGuid().ToString("N")[..6];
        var s1 = "perf-s1-" + agent;
        var s2 = "perf-s2-" + agent;
        await using (var ctx = fixture.DbFactory.CreateDbContext())
        {
            ctx.Escalations.AddRange(
                new EscalationEntity
                {
                    Id = Guid.NewGuid().ToString("N")[..12], SessionId = s1, CreatedAt = From.AddHours(1),
                    AcknowledgedAt = From.AddHours(1).AddMinutes(2), ResolvedAt = From.AddHours(2), Status = "Resolved", AssignedTo = agent
                },
                new EscalationEntity
                {
                    Id = Guid.NewGuid().ToString("N")[..12], SessionId = s2, CreatedAt = From.AddHours(3),
                    AcknowledgedAt = From.AddHours(3).AddMinutes(4), Status = "Acknowledged", AssignedTo = agent
                },
                new EscalationEntity
                {
                    Id = Guid.NewGuid().ToString("N")[..12], SessionId = s2, CreatedAt = From.AddHours(5),
                    Status = "Open", AssignedTo = agent
                });
            ctx.Ratings.AddRange(
                new RatingEntity { SessionId = s1, Id = Guid.NewGuid().ToString("N")[..12], Stars = 5, RatedAt = From.AddHours(2) },
                new RatingEntity { SessionId = s2, Id = Guid.NewGuid().ToString("N")[..12], Stars = 2, RatedAt = From.AddHours(4) });
            ctx.ComplaintEvents.AddRange(
                new ComplaintEventEntity { ComplaintCode = 1001, At = From.AddHours(1), Actor = "x", Kind = "resolved", AssignedAgentId = agent },
                new ComplaintEventEntity { ComplaintCode = 1001, At = From.AddHours(2), Actor = "x", Kind = "rejected", AssignedAgentId = agent },
                new ComplaintEventEntity { ComplaintCode = 1001, At = From.AddHours(2), Actor = "x", Kind = "note", AssignedAgentId = agent },
                new ComplaintEventEntity { ComplaintCode = 1001, At = To.AddHours(1), Actor = "x", Kind = "resolved", AssignedAgentId = agent });
            await ctx.SaveChangesAsync(Ct);
        }

        var row = (await Store.GetHumanAsync(From, To, Ct)).Should().ContainSingle(r => r.AgentId == agent).Subject;

        row.EscalationsTaken.Should().Be(2);
        row.EscalationsResolved.Should().Be(1);
        row.AvgFirstResponseSeconds.Should().BeApproximately(180, 0.5);
        row.AvgResolutionSeconds.Should().BeApproximately(3600, 0.5);
        row.ComplaintsClosed.Should().Be(2);
        row.AvgRating.Should().BeApproximately(3.5, 0.001, "aynı oturumdaki iki eskalasyon puanı iki kez saymamalı");
        row.Ratings.Should().Be(2);
    }

    [Fact]
    public async Task EmptyWindow_ReturnsZeros()
    {
        var far = new DateTime(2040, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var ai = await Store.GetAiAsync(far, far.AddDays(1), Ct);

        ai.Totals.Traces.Should().Be(0);
        ai.Totals.AvgDurationMs.Should().Be(0);
        ai.Turns.Should().BeEmpty();
        (await Store.GetHumanAsync(far, far.AddDays(1), Ct)).Should().BeEmpty();
    }
}

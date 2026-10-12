// Postgres/PostgresPerformanceStore.cs
// Performans ekranının toplama sorguları — trace'lerdeki jsonb dizileri (ajan ziyaretleri, uzman sonuçları,
// araç çağrıları) ve eskalasyon/puan/şikayet olayları. Hesaplama veritabanında yapılır; uygulamaya yalnızca
// gruplanmış satırlar gelir.

using CustomerSupportBot.Adapters.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

public sealed class PostgresPerformanceStore(IDbContextFactory<CustomerSupportDbContext> dbFactory) : IPerformanceStore
{
    // Trace JSON'u PascalCase serileştirilir (PostgresReasoningTraceStore.Serialize). Ajan ziyaretlerindeki ad
    // yürütücü kimliğidir ("OrderAgent_35e6…"); split_part ile örnek soneki atılır.

    public async Task<AiPerformanceData> GetAiAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        var from = Utc(fromUtc);
        var to = Utc(toUtc);
        await using var ctx = await dbFactory.CreateDbContextAsync(ct);

        var totals = (await ctx.Database.SqlQuery<TotalsRow>($"""
            SELECT count(*)::int AS "Traces",
                   (count(*) FILTER (WHERE termination_reason IN ('completed', 'awaiting_user_input')
                                       AND coalesce(error, '') = ''))::int AS "Answered",
                   (count(*) FILTER (WHERE error IS NOT NULL AND error <> ''))::int AS "Errors",
                   coalesce(avg(extract(epoch FROM (completed_at - started_at)) * 1000)
                            FILTER (WHERE completed_at IS NOT NULL), 0)::float8 AS "AvgDurationMs",
                   (count(*) FILTER (WHERE termination_reason = 'escalation_needed'))::int AS "Escalated"
            FROM observability.reasoning_traces
            WHERE started_at >= {from} AND started_at < {to}
            """).ToListAsync(ct)).Single();

        var turns = await ctx.Database.SqlQuery<TurnRow>($"""
            SELECT split_part(v->>'AgentName', '_', 1) AS "AgentName",
                   count(*)::int AS "Turns",
                   coalesce(avg((v->>'DurationMs')::float8), 0)::float8 AS "AvgDurationMs"
            FROM observability.reasoning_traces t
            CROSS JOIN LATERAL jsonb_array_elements(
                CASE WHEN jsonb_typeof(t.agent_visits) = 'array' THEN t.agent_visits ELSE '[]'::jsonb END) v
            WHERE t.started_at >= {from} AND t.started_at < {to} AND coalesce(v->>'AgentName', '') <> ''
            GROUP BY 1
            """).ToListAsync(ct);

        var outcomes = await ctx.Database.SqlQuery<OutcomeRow>($"""
            SELECT s->>'AgentName' AS "AgentName",
                   coalesce(lower(s->'PostToolReflection'->>'Status'), 'unknown') AS "Status",
                   count(*)::int AS "Count"
            FROM observability.reasoning_traces t
            CROSS JOIN LATERAL jsonb_array_elements(
                CASE WHEN jsonb_typeof(t.specialist_reasonings) = 'array' THEN t.specialist_reasonings ELSE '[]'::jsonb END) s
            WHERE t.started_at >= {from} AND t.started_at < {to} AND coalesce(s->>'AgentName', '') <> ''
            GROUP BY 1, 2
            """).ToListAsync(ct);

        var tools = await ctx.Database.SqlQuery<ToolRow>($"""
            SELECT c->>'ToolName' AS "ToolName",
                   max(c->>'AgentName') AS "AgentName",
                   count(*)::int AS "Calls",
                   (count(*) FILTER (WHERE (c->>'Success')::boolean = false))::int AS "Failures",
                   (array_agg(c->>'ResultSummary' ORDER BY t.started_at DESC)
                        FILTER (WHERE (c->>'Success')::boolean = false))[1] AS "LastFailure"
            FROM observability.reasoning_traces t
            CROSS JOIN LATERAL jsonb_array_elements(
                CASE WHEN jsonb_typeof(t.tool_calls) = 'array' THEN t.tool_calls ELSE '[]'::jsonb END) c
            WHERE t.started_at >= {from} AND t.started_at < {to} AND coalesce(c->>'ToolName', '') <> ''
            GROUP BY 1
            """).ToListAsync(ct);

        var terminations = await ctx.Database.SqlQuery<TerminationRow>($"""
            SELECT coalesce(nullif(termination_reason, ''), 'unknown') AS "Reason", count(*)::int AS "Count"
            FROM observability.reasoning_traces
            WHERE started_at >= {from} AND started_at < {to}
            GROUP BY 1
            """).ToListAsync(ct);

        return new AiPerformanceData(
            new TraceTotals(totals.Traces, totals.Answered, totals.Errors, totals.AvgDurationMs, totals.Escalated),
            turns.Select(r => new AgentTurnStat(r.AgentName, r.Turns, r.AvgDurationMs)).ToList(),
            outcomes.Select(r => new AgentOutcomeStat(r.AgentName, r.Status, r.Count)).ToList(),
            tools.Select(r => new ToolStat(r.ToolName, r.AgentName, r.Calls, r.Failures, r.LastFailure)).ToList(),
            terminations.Select(r => new TerminationCount(r.Reason, r.Count)).ToList());
    }

    public async Task<IReadOnlyList<HumanAgentStat>> GetHumanAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        var from = Utc(fromUtc);
        var to = Utc(toUtc);
        await using var ctx = await dbFactory.CreateDbContextAsync(ct);

        // Puanlar oturum başına bir kez sayılır: aynı oturumda birden çok eskalasyon olabilir.
        var rows = await ctx.Database.SqlQuery<HumanRow>($"""
            WITH esc AS (
                SELECT assigned_to AS agent,
                       (count(*) FILTER (WHERE acknowledged_at IS NOT NULL))::int AS taken,
                       (count(*) FILTER (WHERE status = 'Resolved'))::int AS resolved,
                       (avg(extract(epoch FROM (acknowledged_at - created_at)))
                            FILTER (WHERE acknowledged_at IS NOT NULL))::float8 AS first_response,
                       (avg(extract(epoch FROM (resolved_at - created_at)))
                            FILTER (WHERE status = 'Resolved' AND resolved_at IS NOT NULL))::float8 AS resolution
                FROM hitl.escalations
                WHERE assigned_to IS NOT NULL AND created_at >= {from} AND created_at < {to}
                GROUP BY assigned_to
            ),
            rated AS (
                SELECT x.agent, avg(r.stars)::float8 AS avg_rating, count(*)::int AS ratings
                FROM (SELECT DISTINCT assigned_to AS agent, session_id
                      FROM hitl.escalations
                      WHERE assigned_to IS NOT NULL AND session_id IS NOT NULL
                        AND created_at >= {from} AND created_at < {to}) x
                JOIN analytics.ratings r ON r.session_id = x.session_id
                GROUP BY x.agent
            ),
            closed AS (
                SELECT assigned_agent_id AS agent, count(*)::int AS complaints
                FROM catalog.complaint_events
                WHERE kind IN ('resolved', 'rejected') AND assigned_agent_id IS NOT NULL
                  AND at >= {from} AND at < {to}
                GROUP BY assigned_agent_id
            ),
            agents AS (SELECT agent FROM esc UNION SELECT agent FROM closed)
            SELECT a.agent AS "AgentId",
                   coalesce(e.taken, 0) AS "EscalationsTaken",
                   coalesce(e.resolved, 0) AS "EscalationsResolved",
                   e.first_response AS "AvgFirstResponseSeconds",
                   e.resolution AS "AvgResolutionSeconds",
                   coalesce(c.complaints, 0) AS "ComplaintsClosed",
                   r.avg_rating AS "AvgRating",
                   coalesce(r.ratings, 0) AS "Ratings"
            FROM agents a
            LEFT JOIN esc e ON e.agent = a.agent
            LEFT JOIN closed c ON c.agent = a.agent
            LEFT JOIN rated r ON r.agent = a.agent
            """).ToListAsync(ct);

        return rows.Select(r => new HumanAgentStat(r.AgentId, r.EscalationsTaken, r.EscalationsResolved,
            r.AvgFirstResponseSeconds, r.AvgResolutionSeconds, r.ComplaintsClosed, r.AvgRating, r.Ratings)).ToList();
    }

    private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);

    // SqlQuery<T> sütunları özellik adlarıyla eşler; bu yüzden ayarlanabilir özellikli satır tipleri.
    private sealed class TotalsRow { public int Traces { get; set; } public int Answered { get; set; } public int Errors { get; set; } public double AvgDurationMs { get; set; } public int Escalated { get; set; } }
    private sealed class TurnRow { public string AgentName { get; set; } = ""; public int Turns { get; set; } public double AvgDurationMs { get; set; } }
    private sealed class OutcomeRow { public string AgentName { get; set; } = ""; public string Status { get; set; } = ""; public int Count { get; set; } }
    private sealed class ToolRow { public string ToolName { get; set; } = ""; public string? AgentName { get; set; } public int Calls { get; set; } public int Failures { get; set; } public string? LastFailure { get; set; } }
    private sealed class TerminationRow { public string Reason { get; set; } = ""; public int Count { get; set; } }
    private sealed class HumanRow
    {
        public string AgentId { get; set; } = "";
        public int EscalationsTaken { get; set; }
        public int EscalationsResolved { get; set; }
        public double? AvgFirstResponseSeconds { get; set; }
        public double? AvgResolutionSeconds { get; set; }
        public int ComplaintsClosed { get; set; }
        public double? AvgRating { get; set; }
        public int Ratings { get; set; }
    }
}

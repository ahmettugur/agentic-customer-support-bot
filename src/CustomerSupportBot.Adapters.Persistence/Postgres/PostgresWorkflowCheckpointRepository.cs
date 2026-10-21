// Postgres/PostgresWorkflowCheckpointRepository.cs
// IWorkflowCheckpointRepository — kalıcı uygulama (observability.workflow_turns / workflow_checkpoints).

using System.Text.Json;
using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

public sealed class PostgresWorkflowCheckpointRepository(IDbContextFactory<CustomerSupportDbContext> dbFactory)
    : IWorkflowCheckpointRepository
{
    // Npgsql timestamptz yalnızca Kind=Utc kabul eder.
    private static DateTime Utc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public async Task BeginTurnAsync(WorkflowTurnContext turn, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var reasoning = turn.Reasoning is null ? null : JsonSerializer.Serialize(turn.Reasoning);
        var started = Utc(turn.StartedAtUtc);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO observability.workflow_turns (trace_id, session_id, query, customer_id, reasoning, history_count, started_at)
            VALUES ({turn.TraceId}, {turn.SessionId}, {turn.Query}, {turn.CustomerId}, CAST({reasoning} AS jsonb), {turn.HistoryCount}, {started})
            ON CONFLICT (trace_id) DO UPDATE SET
                session_id = EXCLUDED.session_id, query = EXCLUDED.query, customer_id = EXCLUDED.customer_id,
                reasoning = EXCLUDED.reasoning, history_count = EXCLUDED.history_count, started_at = EXCLUDED.started_at
            """, ct);
    }

    public async Task<WorkflowTurnContext?> GetTurnAsync(string traceId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var e = await db.WorkflowTurns.AsNoTracking().FirstOrDefaultAsync(t => t.TraceId == traceId, ct);
        if (e is null) return null;
        var reasoning = string.IsNullOrWhiteSpace(e.ReasoningJson) ? null : JsonSerializer.Deserialize<ReasoningResult>(e.ReasoningJson);
        return new WorkflowTurnContext(e.TraceId, e.SessionId, e.Query, e.CustomerId, reasoning, e.HistoryCount, DateTime.SpecifyKind(e.StartedAt, DateTimeKind.Utc));
    }

    public async Task AddCheckpointAsync(string traceId, string checkpointId, string? parentCheckpointId, string payloadJson,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO observability.workflow_checkpoints (trace_id, checkpoint_id, parent_checkpoint_id, payload, created_at)
            VALUES ({traceId}, {checkpointId}, {parentCheckpointId}, {payloadJson}, {now})
            ON CONFLICT (trace_id, checkpoint_id) DO UPDATE SET
                parent_checkpoint_id = EXCLUDED.parent_checkpoint_id, payload = EXCLUDED.payload, created_at = EXCLUDED.created_at
            """, ct);
    }

    public async Task<string?> GetCheckpointPayloadAsync(string traceId, string checkpointId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.WorkflowCheckpoints.AsNoTracking()
            .Where(c => c.TraceId == traceId && c.CheckpointId == checkpointId)
            .Select(c => c.PayloadJson)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<WorkflowCheckpointEntry>> ListCheckpointsAsync(string traceId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.WorkflowCheckpoints.AsNoTracking()
            .Where(c => c.TraceId == traceId)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new { c.CheckpointId, c.ParentCheckpointId, c.CreatedAt })
            .ToListAsync(ct);
        return rows.Select(r => new WorkflowCheckpointEntry(r.CheckpointId, r.ParentCheckpointId, DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc))).ToList();
    }

    public async Task<IReadOnlyList<WorkflowIdleTurn>> ListTurnsIdleSinceAsync(DateTime cutoffUtc, int max, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var cutoff = Utc(cutoffUtc);
        var rows = await db.WorkflowTurns.AsNoTracking()
            .Select(t => new
            {
                t.TraceId,
                Last = db.WorkflowCheckpoints.Where(c => c.TraceId == t.TraceId).Max(c => (DateTime?)c.CreatedAt) ?? t.StartedAt
            })
            .Where(x => x.Last < cutoff)
            .OrderBy(x => x.Last)
            .Take(max)
            .ToListAsync(ct);
        return rows.Select(r => new WorkflowIdleTurn(r.TraceId, DateTime.SpecifyKind(r.Last, DateTimeKind.Utc))).ToList();
    }

    public async Task DeleteTurnAsync(string traceId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Checkpoint'ler FK cascade ile birlikte gider.
        await db.WorkflowTurns.Where(t => t.TraceId == traceId).ExecuteDeleteAsync(ct);
    }
}

// Adapters.Persistence/InMemory/InMemoryWorkflowCheckpointRepository.cs
// IWorkflowCheckpointRepository için bellek içi uygulama (Persistence:Provider=InMemory, testler).
// Süreç ölünce kaybolur — çökme sonrası devam yalnızca kalıcı (Postgres) depoyla anlamlıdır.

using System.Collections.Concurrent;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;

namespace CustomerSupportBot.Adapters.Persistence.InMemory;

public sealed class InMemoryWorkflowCheckpointRepository : IWorkflowCheckpointRepository
{
    private sealed record Stored(string CheckpointId, string? ParentId, string Payload, DateTime CreatedAtUtc);

    private readonly ConcurrentDictionary<string, WorkflowTurnContext> _turns = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Stored>> _checkpoints = new();

    public Task BeginTurnAsync(WorkflowTurnContext turn, CancellationToken ct = default)
    {
        _turns[turn.TraceId] = turn;
        return Task.CompletedTask;
    }

    public Task<WorkflowTurnContext?> GetTurnAsync(string traceId, CancellationToken ct = default) =>
        Task.FromResult(_turns.GetValueOrDefault(traceId));

    public Task AddCheckpointAsync(string traceId, string checkpointId, string? parentCheckpointId, string payloadJson,
        CancellationToken ct = default)
    {
        var turn = _checkpoints.GetOrAdd(traceId, _ => new());
        turn[checkpointId] = new Stored(checkpointId, parentCheckpointId, payloadJson, DateTime.UtcNow);
        return Task.CompletedTask;
    }

    public Task<string?> GetCheckpointPayloadAsync(string traceId, string checkpointId, CancellationToken ct = default) =>
        Task.FromResult(_checkpoints.TryGetValue(traceId, out var turn) && turn.TryGetValue(checkpointId, out var c) ? c.Payload : null);

    public Task<IReadOnlyList<WorkflowCheckpointEntry>> ListCheckpointsAsync(string traceId, CancellationToken ct = default)
    {
        IReadOnlyList<WorkflowCheckpointEntry> list = _checkpoints.TryGetValue(traceId, out var turn)
            ? turn.Values.OrderBy(c => c.CreatedAtUtc).Select(c => new WorkflowCheckpointEntry(c.CheckpointId, c.ParentId, c.CreatedAtUtc)).ToList()
            : [];
        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<WorkflowIdleTurn>> ListTurnsIdleSinceAsync(DateTime cutoffUtc, int max, CancellationToken ct = default)
    {
        IReadOnlyList<WorkflowIdleTurn> idle = _turns.Values
            .Select(t => new WorkflowIdleTurn(t.TraceId, LastActivity(t)))
            .Where(t => t.LastActivityUtc < cutoffUtc)
            .OrderBy(t => t.LastActivityUtc)
            .Take(max)
            .ToList();
        return Task.FromResult(idle);
    }

    public Task DeleteTurnAsync(string traceId, CancellationToken ct = default)
    {
        _turns.TryRemove(traceId, out _);
        _checkpoints.TryRemove(traceId, out _);
        return Task.CompletedTask;
    }

    private DateTime LastActivity(WorkflowTurnContext turn) =>
        _checkpoints.TryGetValue(turn.TraceId, out var cps) && !cps.IsEmpty
            ? cps.Values.Max(c => c.CreatedAtUtc)
            : turn.StartedAtUtc;
}

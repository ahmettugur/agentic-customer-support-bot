// Adapters.Agents/Checkpointing/RepositoryCheckpointStore.cs
// MAF'ın checkpoint deposu sözleşmesini (JsonCheckpointStore) kalıcı repository'ye bağlar. MAF "oturum kimliği" olarak
// tur (trace) kimliği verilir; böylece bir turun checkpoint'leri tek anahtarla bulunur ve silinir.

using System.Text.Json;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;

namespace CustomerSupportBot.Adapters.Agents.Checkpointing;

internal sealed class RepositoryCheckpointStore(IWorkflowCheckpointRepository repository) : JsonCheckpointStore
{
    public override async ValueTask<CheckpointInfo> CreateCheckpointAsync(string sessionId, JsonElement value, CheckpointInfo? parent = null)
    {
        var checkpointId = Guid.NewGuid().ToString("N");
        await repository.AddCheckpointAsync(sessionId, checkpointId, parent?.CheckpointId, value.GetRawText()).ConfigureAwait(false);
        return new CheckpointInfo(sessionId, checkpointId);
    }

    public override async ValueTask<JsonElement> RetrieveCheckpointAsync(string sessionId, CheckpointInfo key)
    {
        var payload = await repository.GetCheckpointPayloadAsync(sessionId, key.CheckpointId).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Checkpoint bulunamadı: {sessionId}/{key.CheckpointId}");
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.Clone();
    }

    public override async ValueTask<IEnumerable<CheckpointInfo>> RetrieveIndexAsync(string sessionId, CheckpointInfo? withParent = null)
    {
        var entries = await repository.ListCheckpointsAsync(sessionId).ConfigureAwait(false);
        return entries
            .Where(e => withParent is null || e.ParentCheckpointId == withParent.CheckpointId)
            .Select(e => new CheckpointInfo(sessionId, e.CheckpointId))
            .ToList();
    }
}

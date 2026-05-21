// Application/Services/Memory/DisabledSemanticMemoryIngestor.cs
// Null Object pattern — SemanticMemory devre dışıyken kullanılır.
// Service locator anti-pattern'ı yerine açık null-object kaydı kullanır.

using CustomerSupportBot.Domain.Model.Memory;

namespace CustomerSupportBot.Application.Services.Memory;

/// <summary>
/// Semantic memory devre dışı olduğunda DI'a kaydedilen no-op implementasyonu.
/// </summary>
public sealed class DisabledSemanticMemoryIngestor : ISemanticMemoryIngestor
{
    public bool Enabled => false;
    public bool IsConfigured => false;
    public Task EnsureCollectionsAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task UpsertManyAsync(MemoryKind kind, IReadOnlyList<MemoryDocument> docs, CancellationToken ct = default) => Task.CompletedTask;
}

// InMemory/InMemoryConversationDispositionStore.cs — testler için.

using System.Collections.Concurrent;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Adapters.Persistence.InMemory;

public sealed class InMemoryConversationDispositionStore : IConversationDispositionStore, ISessionDataEraser
{
    private readonly ConcurrentDictionary<string, ConversationDisposition> _byId = new();

    public string Name => "conversation-dispositions";

    public Task AddAsync(ConversationDisposition disposition, CancellationToken ct = default)
    {
        _byId[disposition.Id] = disposition;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ConversationDisposition>> ListForSessionAsync(string sessionId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ConversationDisposition>>(
            _byId.Values.Where(d => d.SessionId == sessionId).OrderBy(d => d.ClosedAt).ToList());

    public Task<IReadOnlyDictionary<string, int>> CountByReasonAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<string, int>>(
            _byId.Values.GroupBy(d => d.ReasonCode).ToDictionary(g => g.Key, g => g.Count()));

    public Task<IReadOnlyList<TagUsage>> TopTagsAsync(int limit, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TagUsage>>(_byId.Values
            .SelectMany(d => d.Tags)
            .GroupBy(t => t)
            .Select(g => new TagUsage(g.Key, g.Count()))
            .OrderByDescending(t => t.Count).ThenBy(t => t.Tag, StringComparer.Ordinal)
            .Take(limit)
            .ToList());

    public Task<int> EraseSessionsAsync(IReadOnlyCollection<string> sessionIds, CancellationToken ct = default)
    {
        var erased = 0;
        foreach (var d in _byId.Values.Where(d => sessionIds.Contains(d.SessionId)).ToList())
            if (_byId.TryRemove(d.Id, out _)) erased++;
        return Task.FromResult(erased);
    }
}

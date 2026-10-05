// InMemory/InMemorySavedReplyStore.cs — testler için.

using System.Collections.Concurrent;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Adapters.Persistence.InMemory;

public sealed class InMemorySavedReplyStore : ISavedReplyStore
{
    private readonly ConcurrentDictionary<string, SavedReply> _byId = new();
    private readonly object _gate = new();

    public Task<IReadOnlyList<SavedReply>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SavedReply>>(_byId.Values.ToList());

    public Task<SavedReply?> GetAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(_byId.TryGetValue(id, out var r) ? r : null);

    public Task<bool> ShortcutExistsAsync(string shortcut, string? exceptId = null, CancellationToken ct = default) =>
        Task.FromResult(_byId.Values.Any(r => r.Id != exceptId &&
            string.Equals(r.Shortcut, shortcut, StringComparison.OrdinalIgnoreCase)));

    public Task AddAsync(SavedReply reply, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (reply.Shortcut is not null && _byId.Values.Any(r => string.Equals(r.Shortcut, reply.Shortcut, StringComparison.OrdinalIgnoreCase)))
                throw new SavedReplyShortcutConflictException(reply.Shortcut);
            _byId[reply.Id] = reply;
        }
        return Task.CompletedTask;
    }

    public Task<bool> UpdateAsync(SavedReply reply, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_byId.ContainsKey(reply.Id)) return Task.FromResult(false);
            _byId[reply.Id] = reply;
            return Task.FromResult(true);
        }
    }

    public Task<bool> DeleteAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(_byId.TryRemove(id, out _));
}

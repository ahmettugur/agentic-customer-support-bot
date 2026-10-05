// InMemory/InMemoryAttachmentStore.cs
// Sohbet fotoğraflarının bellek içi deposu — testler ve tek süreçli kurulum için.

using System.Collections.Concurrent;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Adapters.Persistence.InMemory;

public sealed class InMemoryAttachmentStore : IAttachmentStore, ISessionDataEraser
{
    private readonly ConcurrentDictionary<string, ChatAttachment> _byId = new();
    private readonly object _linkGate = new();

    public Task SaveAsync(ChatAttachment attachment, CancellationToken ct = default)
    {
        _byId[attachment.Id] = attachment;
        return Task.CompletedTask;
    }

    public Task<ChatAttachment?> GetAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(_byId.TryGetValue(id, out var a) ? a : null);

    public Task<IReadOnlyList<ChatAttachment>> ListForSessionAsync(string sessionId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ChatAttachment>>(_byId.Values
            .Where(a => a.SessionId == sessionId)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new ChatAttachment
            {
                Id = a.Id, SessionId = a.SessionId, CustomerId = a.CustomerId, ContentType = a.ContentType,
                Description = a.Description, SentAt = a.SentAt, ApprovalId = a.ApprovalId, CreatedAt = a.CreatedAt
            })
            .ToList());

    public Task MarkSentAsync(IReadOnlyCollection<string> ids, DateTime sentAt, CancellationToken ct = default)
    {
        lock (_linkGate)
        {
            foreach (var id in ids)
                if (_byId.TryGetValue(id, out var a) && a.SentAt is null)
                    a.SentAt = sentAt;
        }
        return Task.CompletedTask;
    }

    public Task<bool> DeleteUnsentAsync(string id, CancellationToken ct = default)
    {
        lock (_linkGate)
        {
            return Task.FromResult(_byId.TryGetValue(id, out var a) && a.SentAt is null && _byId.TryRemove(id, out _));
        }
    }

    public Task<int> DeleteCreatedBeforeAsync(DateTime cutoffUtc, CancellationToken ct = default)
    {
        var count = 0;
        foreach (var a in _byId.Values.Where(a => a.CreatedAt < cutoffUtc).ToList())
            if (_byId.TryRemove(a.Id, out _)) count++;
        return Task.FromResult(count);
    }

    public string Name => "attachments";

    public Task<int> EraseSessionsAsync(IReadOnlyCollection<string> sessionIds, CancellationToken ct = default)
    {
        var ids = sessionIds.ToHashSet(StringComparer.Ordinal);
        var count = 0;
        foreach (var a in _byId.Values.Where(a => ids.Contains(a.SessionId)).ToList())
            if (_byId.TryRemove(a.Id, out _)) count++;
        return Task.FromResult(count);
    }

    public Task LinkToApprovalAsync(IReadOnlyCollection<string> ids, string approvalId, CancellationToken ct = default)
    {
        lock (_linkGate)
        {
            foreach (var id in ids)
                if (_byId.TryGetValue(id, out var a) && a.ApprovalId is null)
                    a.ApprovalId = approvalId;
        }
        return Task.CompletedTask;
    }
}

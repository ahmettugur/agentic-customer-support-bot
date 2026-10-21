// Adapters.Persistence/InMemory/InMemoryMessageFeedbackStore.cs — IMessageFeedbackStore bellek içi uygulaması (testler).

using System.Collections.Concurrent;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Adapters.Persistence.InMemory;

public sealed class InMemoryMessageFeedbackStore : IMessageFeedbackStore
{
    private readonly ConcurrentDictionary<(string Session, string Hash), MessageFeedback> _rows = new();

    public Task UpsertAsync(MessageFeedback feedback, CancellationToken ct = default)
    {
        _rows.AddOrUpdate((feedback.SessionId, feedback.MessageHash),
            _ => Copy(feedback, feedback.CreatedAt, feedback.UpdatedAt),
            // İlk oluşturma zamanı korunur; UpdatedAt en az CreatedAt'ten sonradır (aynı anda yazılmış olsa bile).
            (_, existing) => Copy(feedback, existing.CreatedAt,
                feedback.UpdatedAt > existing.CreatedAt ? feedback.UpdatedAt : existing.CreatedAt.AddTicks(1)));
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string sessionId, string messageHash, CancellationToken ct = default)
    {
        _rows.TryRemove((sessionId, messageHash), out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MessageFeedback>> GetForSessionAsync(string sessionId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<MessageFeedback>>(
            _rows.Values.Where(f => f.SessionId == sessionId).OrderBy(f => f.CreatedAt).Select(f => Copy(f, f.CreatedAt, f.UpdatedAt)).ToList());

    public Task<IReadOnlyList<MessageFeedback>> GetRecentAsync(int count, bool? helpful = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<MessageFeedback>>(
            _rows.Values.Where(f => helpful is null || f.Helpful == helpful)
                .OrderByDescending(f => f.UpdatedAt).Take(count).Select(f => Copy(f, f.CreatedAt, f.UpdatedAt)).ToList());

    public Task<IReadOnlyList<MessageFeedback>> GetUnhelpfulSinceAsync(DateTime sinceUtc, int max, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<MessageFeedback>>(
            _rows.Values.Where(f => !f.Helpful && f.UpdatedAt >= sinceUtc)
                .OrderByDescending(f => f.UpdatedAt).Take(max).Select(f => Copy(f, f.CreatedAt, f.UpdatedAt)).ToList());

    private static MessageFeedback Copy(MessageFeedback f, DateTime created, DateTime updated) => new()
    {
        SessionId = f.SessionId, MessageHash = f.MessageHash, Helpful = f.Helpful, Reason = f.Reason, Comment = f.Comment,
        TraceId = f.TraceId, CustomerId = f.CustomerId, CreatedAt = created, UpdatedAt = updated
    };
}

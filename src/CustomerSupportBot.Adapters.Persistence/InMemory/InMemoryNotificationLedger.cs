// InMemory/InMemoryNotificationLedger.cs
// Bildirim defterinin bellek içi karşılığı — testler ve tek süreçli kurulum için.

using System.Collections.Concurrent;

namespace CustomerSupportBot.Adapters.Persistence.InMemory;

public sealed class InMemoryNotificationLedger : INotificationLedger
{
    private readonly ConcurrentDictionary<string, DateTime> _claims = new(StringComparer.Ordinal);

    public Task<bool> TryClaimAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(_claims.TryAdd(key, DateTime.UtcNow));

    public Task ReleaseAsync(string key, CancellationToken ct = default)
    {
        _claims.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}

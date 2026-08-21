// Tests/Helpers/InMemoryRefreshTokenRepository.cs
// Test-only IRefreshTokenRepository. Üretimde her zaman EfRefreshTokenRepository kullanılır;
// bu yalnızca API entegrasyon testlerinin EF InMemory provider'ı için var — TryRevokeAsync'in
// dayandığı ExecuteUpdateAsync o provider'da desteklenmiyor. Koşullu sahiplenme semantiği
// burada elle (kilit altında) uygulanır ki gerçek HTTP uçları rotasyon davranışını uçtan
// uca sınayabilsin.

using System.Collections.Concurrent;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Domain.Model.Auth;

namespace CustomerSupportBot.Tests.Shared;

public sealed class InMemoryRefreshTokenRepository : IRefreshTokenRepository
{
    private sealed class Entry
    {
        public required string Id;
        public required string UserId;
        public required string TokenHash;
        public required DateTime ExpiresAt;
        public required DateTime CreatedAt;
        public DateTime? RevokedAt;
        public string? ReplacedByTokenHash;
    }

    private readonly ConcurrentDictionary<string, Entry> _byId = new();
    private readonly object _lock = new();

    public Task CreateAsync(string id, string userId, string tokenHash,
        DateTime expiresAt, DateTime createdAt, CancellationToken ct = default)
    {
        _byId[id] = new Entry
        {
            Id = id, UserId = userId, TokenHash = tokenHash,
            ExpiresAt = expiresAt, CreatedAt = createdAt
        };
        return Task.CompletedTask;
    }

    public Task<RefreshTokenInfo?> FindByHashAsync(string tokenHash, CancellationToken ct = default)
    {
        var entry = _byId.Values.FirstOrDefault(e => e.TokenHash == tokenHash);
        return Task.FromResult(entry is null ? null : ToInfo(entry));
    }

    public Task RevokeAsync(string id, DateTime revokedAt,
        string? replacedByTokenHash, CancellationToken ct = default)
    {
        if (_byId.TryGetValue(id, out var entry))
        {
            lock (_lock)
            {
                entry.RevokedAt = revokedAt;
                entry.ReplacedByTokenHash = replacedByTokenHash;
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>Gerçek deponun koşullu UPDATE'iyle aynı sözleşme: yalnızca hâlâ iptal edilmemişse iptal eder.</summary>
    public Task<bool> TryRevokeAsync(string id, DateTime revokedAt,
        string? replacedByTokenHash, CancellationToken ct = default)
    {
        if (!_byId.TryGetValue(id, out var entry)) return Task.FromResult(false);

        lock (_lock)
        {
            if (entry.RevokedAt is not null) return Task.FromResult(false);
            entry.RevokedAt = revokedAt;
            entry.ReplacedByTokenHash = replacedByTokenHash;
            return Task.FromResult(true);
        }
    }

    private static RefreshTokenInfo ToInfo(Entry e) =>
        new(e.Id, e.UserId, e.TokenHash, e.ExpiresAt, e.CreatedAt, e.RevokedAt, e.ReplacedByTokenHash);
}

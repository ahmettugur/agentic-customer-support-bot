// Adapters.Redis/Locking/RedisDistributedLockAdapter.cs
// DRIVEN ADAPTER — IAppDistributedLock → Redis (Medallion RedLock) implementasyonu.
// Core bu adapter'ı bilmez; sadece IAppDistributedLock'a bağımlıdır.

using CustomerSupportBot.Application.Ports.Driven.Locking;
using Medallion.Threading.Redis;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CustomerSupportBot.Adapters.Redis.Locking;

/// <summary>
/// Redis tabanlı dağıtık kilit adapter'ı.
/// Medallion.Threading.Redis (RedLock algoritması) kullanır.
/// </summary>
public sealed class RedisDistributedLockAdapter : IAppDistributedLock
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisDistributedLockAdapter> _logger;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    public RedisDistributedLockAdapter(
        IConnectionMultiplexer redis,
        ILogger<RedisDistributedLockAdapter> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<IAsyncDisposable?> TryAcquireAsync(
        string resourceKey,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var @lock = new RedisDistributedLock(resourceKey, _redis.GetDatabase());
        var handle = await @lock.TryAcquireAsync(timeout ?? DefaultTimeout, ct);

        if (handle is null)
        {
            _logger.LogDebug("Lock alınamadı: {Key}", resourceKey);
            return null;
        }

        return new LockHandle(handle);
    }

    public async Task<IAsyncDisposable> AcquireAsync(
        string resourceKey,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        try
        {
            var handle = await TryAcquireAsync(resourceKey, timeout, ct);
            if (handle is null)
                throw ExceptionTranslator.Translate(
                    new TimeoutException($"Distributed lock alınamadı: '{resourceKey}'"),
                    $"Lock acquire timeout: {resourceKey}");
            return handle;
        }
        catch (RedisException ex)
        {
            throw ExceptionTranslator.Translate(ex, $"Lock acquire hatası: {resourceKey}");
        }
    }

    private sealed class LockHandle : IAsyncDisposable
    {
        private readonly IAsyncDisposable _inner;

        public LockHandle(IAsyncDisposable inner) => _inner = inner;

        public async ValueTask DisposeAsync() => await _inner.DisposeAsync();
    }
}

using CustomerSupportBot.Api.Models;
// Services/Locking/RedisDistributedLock.cs
// Redis + Medallion DistributedLock.Redis tabanlı distributed lock implementasyonu.
// Multi-instance (horizontal scale) ortamlarda güvenli lock mekanizması sağlar.
//
// Best practices:
//   - Lock key'e prefix eklenir (multi-tenant collision önlemi)
//   - Expiry süresi config'den gelir (deadlock koruması)
//   - TryAcquire + timeout pattern (bekleme sınırı)
//   - IAsyncDisposable handle (otomatik release, exception-safe)
//   - Fallback: Redis bağlantı hatası loglanır, null döner (graceful degradation)

using CustomerSupportBot.Domain.Model;
using Medallion.Threading;
using Medallion.Threading.Redis;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace CustomerSupportBot.Api.Services.Locking;

public sealed class RedisDistributedLock : IAppDistributedLock
{
    private readonly RedisDistributedSynchronizationProvider _provider;
    private readonly string _keyPrefix;
    private readonly TimeSpan _defaultTimeout;
    private readonly TimeSpan _lockExpiry;
    private readonly ILogger<RedisDistributedLock> _logger;

    public RedisDistributedLock(
        IConnectionMultiplexer redis,
        IOptions<RedisOptions> options,
        ILogger<RedisDistributedLock> logger)
    {
        _logger = logger;
        var opts = options.Value;
        _keyPrefix = string.IsNullOrWhiteSpace(opts.KeyPrefix) ? "csbot" : opts.KeyPrefix;
        _defaultTimeout = TimeSpan.FromSeconds(Math.Max(1, opts.DefaultLockTimeoutSeconds));
        _lockExpiry = TimeSpan.FromSeconds(Math.Max(5, opts.LockExpirySeconds));

        // Medallion RedisDistributedSynchronizationProvider —
        // her CreateLock çağrısında Redis key üzerinden lock yönetir.
        _provider = new RedisDistributedSynchronizationProvider(
            redis.GetDatabase(),
            options: o => o.Expiry(_lockExpiry));
    }

    public async Task<IAsyncDisposable?> TryAcquireAsync(
        string resourceKey,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var fullKey = $"{_keyPrefix}:lock:{resourceKey}";
        var effectiveTimeout = timeout ?? _defaultTimeout;

        try
        {
            var handle = await _provider
                .CreateLock(fullKey)
                .TryAcquireAsync(effectiveTimeout, ct)
                .ConfigureAwait(false);

            if (handle == null)
            {
                _logger.LogDebug("[DistributedLock] Lock alınamadı: {Key} (timeout: {Timeout})",
                    fullKey, effectiveTimeout);
                return null;
            }

            return new MedallionHandleWrapper(handle);
        }
        catch (OperationCanceledException)
        {
            throw; // CancellationToken — yukarıya propagate et
        }
        catch (Exception ex)
        {
            // Redis bağlantı hatası — graceful degradation, null dön
            _logger.LogWarning(ex,
                "[DistributedLock] Redis lock hatası: {Key}. Lock alınamadı — işlem korumasız devam edecek.",
                fullKey);
            return null;
        }
    }

    public async Task<IAsyncDisposable> AcquireAsync(
        string resourceKey,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var handle = await TryAcquireAsync(resourceKey, timeout, ct).ConfigureAwait(false);
        if (handle == null)
        {
            var fullKey = $"{_keyPrefix}:lock:{resourceKey}";
            throw new TimeoutException(
                $"Redis lock '{fullKey}' {timeout ?? _defaultTimeout} süresi içinde alınamadı.");
        }
        return handle;
    }

    /// <summary>Medallion IDistributedSynchronizationHandle → IAsyncDisposable adapter.</summary>
    private sealed class MedallionHandleWrapper : IAsyncDisposable
    {
        private IDistributedSynchronizationHandle? _handle;

        public MedallionHandleWrapper(IDistributedSynchronizationHandle handle) => _handle = handle;

        public async ValueTask DisposeAsync()
        {
            var h = Interlocked.Exchange(ref _handle, null);
            if (h != null)
            {
                await h.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}


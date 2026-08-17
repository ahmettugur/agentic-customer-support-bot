using CustomerSupportBot.Adapters.Redis;
// Tests/Helpers/InMemoryDistributedLock.cs
// Test-only in-process distributed lock implementasyonu.
// SemaphoreSlim(1,1) per-key kullanır — async-friendly ve reentrant-safe.
// Production'da bu sınıf KULLANILMAZ — yalnızca birim testleri Redis'e bağımlı olmadan çalıştırmak içindir.

using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Tests.Helpers;

public sealed class InMemoryDistributedLock : IAppDistributedLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new(StringComparer.Ordinal);
    private readonly TimeSpan _defaultTimeout;

    public InMemoryDistributedLock(IOptions<RedisOptions> options)
    {
        _defaultTimeout = TimeSpan.FromSeconds(
            Math.Max(1, options.Value.DefaultLockTimeoutSeconds));
    }

    public async Task<IAsyncDisposable?> TryAcquireAsync(
        string resourceKey,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var semaphore = _semaphores.GetOrAdd(resourceKey, _ => new SemaphoreSlim(1, 1));
        var effectiveTimeout = timeout ?? _defaultTimeout;

        var acquired = await semaphore.WaitAsync(effectiveTimeout, ct).ConfigureAwait(false);
        if (!acquired) return null;

        return new SemaphoreReleaser(semaphore);
    }

    public async Task<IAsyncDisposable> AcquireAsync(
        string resourceKey,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var handle = await TryAcquireAsync(resourceKey, timeout, ct).ConfigureAwait(false);
        if (handle == null)
        {
            throw new TimeoutException(
                $"InMemory lock '{resourceKey}' {timeout ?? _defaultTimeout} süresi içinde alınamadı.");
        }
        return handle;
    }

    /// <summary>IAsyncDisposable handle — dispose edildiğinde SemaphoreSlim release olur.</summary>
    private sealed class SemaphoreReleaser : IAsyncDisposable
    {
        private SemaphoreSlim? _semaphore;

        public SemaphoreReleaser(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public ValueTask DisposeAsync()
        {
            var sem = Interlocked.Exchange(ref _semaphore, null);
            sem?.Release();
            return ValueTask.CompletedTask;
        }
    }
}

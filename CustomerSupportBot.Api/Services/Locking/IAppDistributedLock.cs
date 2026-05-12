// Services/Locking/IAppDistributedLock.cs
// Uygulama genelinde distributed lock soyutlaması.
// Production'da RedisDistributedLock kullanılır (Redis zorunludur).
// Best practice: lock'lar IAsyncDisposable scope ile yönetilir — using block sonunda
// otomatik release olur. Bu pattern, exception veya CancellationToken senaryolarında
// lock'un sızmamasını garanti eder.

namespace CustomerSupportBot.Api.Services.Locking;

/// <summary>
/// Distributed lock soyutlaması.
/// Kullanım:
/// <code>
/// await using var handle = await _lock.TryAcquireAsync("session:abc", timeout, ct);
/// if (handle != null) { /* protected region */ }
/// </code>
/// </summary>
public interface IAppDistributedLock
{
    /// <summary>
    /// Verilen resource key için lock almayı dener.
    /// Lock alınırsa <see cref="IAsyncDisposable"/> handle döner — dispose edildiğinde release olur.
    /// Timeout süresi içinde alınamazsa <c>null</c> döner.
    /// </summary>
    /// <param name="resourceKey">Lock key (ör. "session:abc", "profile:CUST-1990").</param>
    /// <param name="timeout">Maksimum bekleme süresi. Null ise varsayılan config değeri kullanılır.</param>
    /// <param name="ct">İptal token'ı.</param>
    /// <returns>Lock handle veya null (alınamadıysa).</returns>
    Task<IAsyncDisposable?> TryAcquireAsync(
        string resourceKey,
        TimeSpan? timeout = null,
        CancellationToken ct = default);

    /// <summary>
    /// Lock alana kadar bekler. Timeout aşılırsa <see cref="TimeoutException"/> fırlatır.
    /// Critical section'lar için kullanılır — lock alınamazsa devam etmek anlamsızsa.
    /// </summary>
    Task<IAsyncDisposable> AcquireAsync(
        string resourceKey,
        TimeSpan? timeout = null,
        CancellationToken ct = default);
}

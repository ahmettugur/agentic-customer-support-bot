// Ports/Driven/Locking/IAppDistributedLock.cs
// SECONDARY PORT — Dağıtık kilit soyutlaması.
// Adaptör: RedisDistributedLockAdapter (CustomerSupportBot.Adapters.Redis)

namespace CustomerSupportBot.Application.Ports.Driven.Locking;

/// <summary>
/// Dağıtık kilit için secondary port.
/// Core concurrent state mutasyonlarını bu port üzerinden serialize eder.
/// Adaptörler: RedisDistributedLockAdapter, InMemorySemaphoreAdapter.
/// </summary>
public interface IAppDistributedLock
{
    /// <summary>
    /// Verilen resource key için kilit almayı dener.
    /// Kilit alınırsa IAsyncDisposable handle döner; dispose edildiğinde release olur.
    /// Timeout içinde alınamazsa null döner.
    /// </summary>
    Task<IAsyncDisposable?> TryAcquireAsync(
        string resourceKey,
        TimeSpan? timeout = null,
        CancellationToken ct = default);

    /// <summary>Kilit alana kadar bekler. Timeout aşılırsa TimeoutException fırlatır.</summary>
    Task<IAsyncDisposable> AcquireAsync(
        string resourceKey,
        TimeSpan? timeout = null,
        CancellationToken ct = default);
}

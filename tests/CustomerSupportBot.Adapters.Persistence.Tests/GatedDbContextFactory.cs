// Tests/GatedDbContextFactory.cs
// İlk DbContext açılışını, testin izin verdiği ana kadar bekleten factory.
//
// Yarış testlerinde "yavaş DB" gerekiyor ama Task.Delay ile kurulan bir yavaşlık kırılgandır:
// makine yavaşladığında ya da hızlandığında test rastgele geçer/kalır. Burada zamanlama
// yerine AÇIK BİR KAPI var — ilk çağıran kapıda bekler, testi yazan kapıyı ne zaman açacağına
// kendisi karar verir. Böylece yarışın hangi anda gözlendiği deterministiktir.

using CustomerSupportBot.Adapters.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

public sealed class GatedDbContextFactory : IDbContextFactory<CustomerSupportDbContext>
{
    private readonly IDbContextFactory<CustomerSupportDbContext> _inner;
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _calls;

    public GatedDbContextFactory(IDbContextFactory<CustomerSupportDbContext> inner) => _inner = inner;

    /// <summary>İlk çağıranın kapıya ULAŞTIĞINI bildirir — test o ana kadar bekleyebilir.</summary>
    public Task FirstCallEntered => _entered.Task;

    public void OpenGate() => _gate.TrySetResult();

    public CustomerSupportDbContext CreateDbContext() => _inner.CreateDbContext();

    public async Task<CustomerSupportDbContext> CreateDbContextAsync(CancellationToken ct = default)
    {
        if (Interlocked.Increment(ref _calls) == 1)
        {
            _entered.TrySetResult();
            await _gate.Task.ConfigureAwait(false);
        }

        return await _inner.CreateDbContextAsync(ct).ConfigureAwait(false);
    }
}

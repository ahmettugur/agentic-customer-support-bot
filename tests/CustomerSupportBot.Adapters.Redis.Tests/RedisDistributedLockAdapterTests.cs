// Tests/RedisDistributedLockAdapterTests.cs
//
// Not — fencing token: Medallion RedLock tutulduğu sürece kira süresini arka planda
// uzatır; bu yüzden uzun süren bir kritik bölge, kilit "sessizce" süresi dolup başka bir
// pod'a geçmeden korunur. Süreç donması (GC duraklaması, ağ bölünmesi) gibi uzatmanın
// yetişemediği durumlara karşı fencing token YOKTUR — kilitle korunan yazmalar ayrıca
// DB tarafında koşullu UPDATE / unique index ile korunur (bkz. PostgresApprovalQueue,
// PostgresEscalationSink).

using CustomerSupportBot.Adapters.Redis.Locking;
using CustomerSupportBot.Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Adapters.Redis.Tests;

[Collection("Redis")]
public class RedisDistributedLockAdapterTests(RedisFixture fixture)
{
    private async Task<RedisDistributedLockAdapter> NewPodAsync() =>
        new(await fixture.ConnectAsync(), NullLogger<RedisDistributedLockAdapter>.Instance);

    private static string Key() => $"test:lock:{Guid.NewGuid():N}";

    [Fact]
    public async Task TryAcquire_HeldByOtherPod_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var key = Key();
        var podA = await NewPodAsync();
        var podB = await NewPodAsync();

        await using var held = await podA.TryAcquireAsync(key, TimeSpan.Zero, ct);
        held.Should().NotBeNull();

        var second = await podB.TryAcquireAsync(key, TimeSpan.FromMilliseconds(200), ct);
        second.Should().BeNull("aynı anahtar başka bir pod'da tutuluyor");
    }

    [Fact]
    public async Task TryAcquire_AfterRelease_Succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var key = Key();
        var podA = await NewPodAsync();
        var podB = await NewPodAsync();

        var held = await podA.TryAcquireAsync(key, TimeSpan.Zero, ct);
        await held!.DisposeAsync();

        await using var second = await podB.TryAcquireAsync(key, TimeSpan.FromSeconds(1), ct);
        second.Should().NotBeNull();
    }

    [Fact]
    public async Task Acquire_WaitsForReleaseWithinTimeout()
    {
        var ct = TestContext.Current.CancellationToken;
        var key = Key();
        var podA = await NewPodAsync();
        var podB = await NewPodAsync();

        var held = await podA.AcquireAsync(key, TimeSpan.Zero, ct);
        var waiter = podB.AcquireAsync(key, TimeSpan.FromSeconds(10), ct);

        await Task.Delay(300, ct);
        waiter.IsCompleted.Should().BeFalse("kilit hâlâ A'da");

        await held.DisposeAsync();
        await using var acquired = await waiter;
        acquired.Should().NotBeNull();
    }

    [Fact]
    public async Task Acquire_Timeout_ThrowsDomainException()
    {
        var ct = TestContext.Current.CancellationToken;
        var key = Key();
        var podA = await NewPodAsync();
        var podB = await NewPodAsync();

        await using var held = await podA.AcquireAsync(key, TimeSpan.Zero, ct);

        var act = () => podB.AcquireAsync(key, TimeSpan.FromMilliseconds(200), ct);
        await act.Should().ThrowAsync<ExternalServiceException>();
    }

    [Fact]
    public async Task CriticalSection_IsMutuallyExclusiveAcrossPods()
    {
        var ct = TestContext.Current.CancellationToken;
        var key = Key();
        var pods = new List<RedisDistributedLockAdapter>();
        for (var i = 0; i < 4; i++) pods.Add(await NewPodAsync());

        var inside = 0;
        var maxInside = 0;

        await Task.WhenAll(pods.SelectMany(pod => Enumerable.Range(0, 5).Select(_ => Task.Run(async () =>
        {
            await using var h = await pod.AcquireAsync(key, TimeSpan.FromSeconds(30), ct);
            var now = Interlocked.Increment(ref inside);
            InterlockedMax(ref maxInside, now);
            await Task.Delay(10, ct);
            Interlocked.Decrement(ref inside);
        }, ct))));

        maxInside.Should().Be(1);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value
               && Interlocked.CompareExchange(ref target, value, current) != current) { }
    }
}

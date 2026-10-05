// LLM harcama sayaçları — gerçek Redis: pod'lar arası toplam, ilk yazımda TTL, yalnız yoksa tohumlama.

using CustomerSupportBot.Adapters.Redis.Budget;

namespace CustomerSupportBot.Adapters.Redis.Tests;

[Collection("Redis")]
public class RedisLlmSpendCounterTests(RedisFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Key() => $"test:{Guid.NewGuid():N}";

    [Fact]
    public async Task Add_AccumulatesAcrossPods()
    {
        var key = Key();
        var podA = new RedisLlmSpendCounter(await fixture.ConnectAsync());
        var podB = new RedisLlmSpendCounter(await fixture.ConnectAsync());

        (await podA.GetAsync(key, Ct)).Should().BeNull();
        (await podA.AddAsync(key, 0.0125m, TimeSpan.FromMinutes(5), Ct)).Should().Be(0.0125m);
        (await podB.AddAsync(key, 1.5m, TimeSpan.FromMinutes(5), Ct)).Should().Be(1.5125m);

        (await podA.GetAsync(key, Ct)).Should().Be(1.5125m);
    }

    [Fact]
    public async Task Ttl_IsSetOnTheFirstWrite_AndNotExtendedLater()
    {
        var key = Key();
        var mux = await fixture.ConnectAsync();
        var counter = new RedisLlmSpendCounter(mux);

        await counter.AddAsync(key, 1m, TimeSpan.FromSeconds(100), Ct);
        await counter.AddAsync(key, 1m, TimeSpan.FromHours(10), Ct);

        var ttl = await mux.GetDatabase().KeyTimeToLiveAsync(RedisLlmSpendCounter.Prefix + key);
        ttl.Should().NotBeNull();
        ttl!.Value.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(100), "dönem anahtarı dönem sonunda düşmeli");
    }

    [Fact]
    public async Task Seed_OnlyWhenMissing()
    {
        var key = Key();
        var counter = new RedisLlmSpendCounter(await fixture.ConnectAsync());

        await counter.SeedAsync(key, 7m, TimeSpan.FromMinutes(5), Ct);
        await counter.SeedAsync(key, 99m, TimeSpan.FromMinutes(5), Ct);
        await counter.AddAsync(key, 1m, TimeSpan.FromMinutes(5), Ct);

        (await counter.GetAsync(key, Ct)).Should().Be(8m);
    }
}

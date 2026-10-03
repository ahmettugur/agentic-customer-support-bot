// Tests/RedisIdempotencyStoreTests.cs
// Pod'lar arası yan etki idempotency kaydı (SideEffectIdempotencyCache'in dağıtık katmanı).

using CustomerSupportBot.Adapters.Redis.Idempotency;

namespace CustomerSupportBot.Adapters.Redis.Tests;

[Collection("Redis")]
public class RedisIdempotencyStoreTests(RedisFixture fixture)
{
    private static string Key() => $"order:{Guid.NewGuid():N}";

    [Fact]
    public async Task Set_IsVisibleFromAnotherPod()
    {
        var key = Key();
        var podA = new RedisIdempotencyStore(await fixture.ConnectAsync());
        var podB = new RedisIdempotencyStore(await fixture.ConnectAsync());

        podA.Set(key, "ORD-1", TimeSpan.FromMinutes(1));

        podB.Get(key).Should().Be("ORD-1");
    }

    [Fact]
    public async Task Set_FirstWriterWins()
    {
        var key = Key();
        var store = new RedisIdempotencyStore(await fixture.ConnectAsync());

        store.Set(key, "ORD-1", TimeSpan.FromMinutes(1));
        store.Set(key, "ORD-2", TimeSpan.FromMinutes(1));

        store.Get(key).Should().Be("ORD-1", "ilk oluşturulan kayıt kanonik — sonraki yazma ezmemeli");
    }

    [Fact]
    public async Task Get_AfterTtl_ReturnsNull()
    {
        var key = Key();
        var store = new RedisIdempotencyStore(await fixture.ConnectAsync());

        store.Set(key, "ORD-1", TimeSpan.FromMilliseconds(150));
        await Task.Delay(400, TestContext.Current.CancellationToken);

        store.Get(key).Should().BeNull();
    }

    [Fact]
    public async Task Get_UnknownKey_ReturnsNull()
    {
        var store = new RedisIdempotencyStore(await fixture.ConnectAsync());
        store.Get(Key()).Should().BeNull();
    }
}

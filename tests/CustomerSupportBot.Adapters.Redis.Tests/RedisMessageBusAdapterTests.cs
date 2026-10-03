// Tests/RedisMessageBusAdapterTests.cs
// Pod'lar arası cache senkronizasyonunun taşıyıcısı — gerçek Redis PUBLISH/SUBSCRIBE.

using CustomerSupportBot.Adapters.Redis.Messaging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Adapters.Redis.Tests;

[Collection("Redis")]
public class RedisMessageBusAdapterTests(RedisFixture fixture)
{
    private async Task<RedisMessageBusAdapter> NewPodAsync() =>
        new(await fixture.ConnectAsync(), NullLogger<RedisMessageBusAdapter>.Instance);

    [Fact]
    public async Task Publish_ReachesSubscriberOnAnotherPod()
    {
        var channel = $"test:{Guid.NewGuid():N}";
        var podA = await NewPodAsync();
        var podB = await NewPodAsync();
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        podB.Subscribe(channel, payload => received.TrySetResult(payload));
        await Task.Delay(200, TestContext.Current.CancellationToken); // SUBSCRIBE'ın sunucuya ulaşması

        podA.Publish(channel, """{"x":1}""");

        (await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken))
            .Should().Be("""{"x":1}""");
    }

    [Fact]
    public async Task ThrowingHandler_DoesNotBreakLaterDeliveries()
    {
        var channel = $"test:{Guid.NewGuid():N}";
        var podA = await NewPodAsync();
        var podB = await NewPodAsync();
        var calls = 0;
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        podB.Subscribe(channel, _ =>
        {
            if (Interlocked.Increment(ref calls) == 1) throw new InvalidOperationException("boom");
            second.TrySetResult();
        });
        await Task.Delay(200, TestContext.Current.CancellationToken);

        podA.Publish(channel, "1");
        podA.Publish(channel, "2");

        await second.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        calls.Should().Be(2);
    }

    [Fact]
    public async Task NodeIds_AreDistinctPerPod()
    {
        var podA = await NewPodAsync();
        var podB = await NewPodAsync();
        podA.NodeId.Should().NotBe(podB.NodeId, "pod'lar kendi yayınlarını NodeId ile ayıklar");
    }
}

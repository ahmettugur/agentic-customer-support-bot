// Ajan/Reasoning istemcisi çağrıdan dönen token kullanımını etiketiyle tur biriktiricisine yazar — hem tek seferlik hem akış.

using CustomerSupportBot.Adapters.Agents;
using CustomerSupportBot.Application.Services.Telemetry;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Adapters.Agents.Tests;

public class UsageLabelingChatClientTests
{
    private readonly LlmCallAttribution _attribution = new();

    private sealed class FakeClient(long input, long output) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
            { Usage = new UsageDetails { InputTokenCount = input, OutputTokenCount = output } });

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "o");
            yield return new ChatResponseUpdate { Contents = [new UsageContent(new UsageDetails { InputTokenCount = input, OutputTokenCount = output })] };
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task NonStreaming_RecordsUsageUnderTheLabel()
    {
        var tracker = new TurnUsageTracker(_attribution);
        tracker.Begin("s1");
        var client = new UsageLabelingChatClient(new FakeClient(700, 40), "ProductAgent", tracker);

        using (_attribution.BeginSession("s1"))
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "x")], cancellationToken: TestContext.Current.CancellationToken);

        var line = tracker.Complete("s1")!.Lines.Single();
        (line.Label, line.InputTokens, line.OutputTokens, line.Calls).Should().Be(("ProductAgent", 700L, 40L, 1));
    }

    [Fact]
    public async Task Streaming_RecordsUsageOnceAfterTheStreamEnds()
    {
        var tracker = new TurnUsageTracker(_attribution);
        tracker.Begin("s1");
        var client = new UsageLabelingChatClient(new FakeClient(300, 20), "Reasoning", tracker);

        using (_attribution.BeginSession("s1"))
        {
            await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "x")], cancellationToken: TestContext.Current.CancellationToken)) { }
        }

        var line = tracker.Complete("s1")!.Lines.Single();
        (line.Label, line.InputTokens, line.OutputTokens, line.Calls).Should().Be(("Reasoning", 300L, 20L, 1));
    }

    [Fact]
    public async Task ResponseWithoutUsage_IsNotRecorded()
    {
        var tracker = new TurnUsageTracker(_attribution);
        tracker.Begin("s1");
        var client = new UsageLabelingChatClient(new FakeClient(0, 0), "ResponseAgent", tracker);

        using (_attribution.BeginSession("s1"))
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "x")], cancellationToken: TestContext.Current.CancellationToken);

        tracker.Complete("s1").Should().BeNull();
    }
}

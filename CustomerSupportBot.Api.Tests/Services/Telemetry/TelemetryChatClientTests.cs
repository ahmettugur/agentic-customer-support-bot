using CustomerSupportBot.Api.Services.Telemetry;
using CustomerSupportBot.Api.Services.Telemetry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Api.Tests.Services.Telemetry;

public class TelemetryChatClientTests
{
    private static (TelemetryChatClient Client, CostUsageStore Store, FakeChatClient Inner) Build(
        decimal estimatedCost = 0.01m)
    {
        var inner = new FakeChatClient();
        var calculator = Substitute.For<ICostCalculator>();
        calculator.Estimate(Arg.Any<string?>(), Arg.Any<long>(), Arg.Any<long>()).Returns(estimatedCost);
        var store = new CostUsageStore();
        var client = new TelemetryChatClient(inner, calculator, store, "gpt-x", "OpenAI", NullLogger<TelemetryChatClient>.Instance);
        return (client, store, inner);
    }

    [Fact]
    public async Task GetResponseAsync_RecordsTokensAndCost()
    {
        var (client, store, inner) = Build(estimatedCost: 0.0123m);
        inner.Response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
        {
            ModelId = "gpt-x-actual",
            Usage = new UsageDetails { InputTokenCount = 1234, OutputTokenCount = 567 }
        };

        var resp = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken);

        resp.Text.Should().Be("ok");
        var snap = store.GetSnapshot();
        snap.TotalCalls.Should().Be(1);
        snap.TotalInputTokens.Should().Be(1234);
        snap.TotalOutputTokens.Should().Be(567);
        snap.TotalCostUsd.Should().Be(0.0123m);
        snap.ByModel.Single().Model.Should().Be("gpt-x-actual");
    }

    [Fact]
    public async Task GetResponseAsync_FallsBackToModelHintWhenResponseHasNoModelId()
    {
        var (client, store, inner) = Build();
        inner.Response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
        {
            Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 }
        };

        await client.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "hi") }, cancellationToken: TestContext.Current.CancellationToken);

        store.GetSnapshot().ByModel.Single().Model.Should().Be("gpt-x");
    }

    [Fact]
    public async Task GetResponseAsync_OnException_DoesNotRecordAndRethrows()
    {
        var (client, store, inner) = Build();
        inner.ThrowOnCall = new InvalidOperationException("boom");

        var act = async () => await client.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "hi") });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        store.GetSnapshot().TotalCalls.Should().Be(0);
    }

    [Fact]
    public async Task StreamingResponse_AggregatesUsageFromUpdates()
    {
        var (client, store, inner) = Build(estimatedCost: 0.0042m);
        inner.StreamingUpdates = new List<ChatResponseUpdate>
        {
            new(ChatRole.Assistant, "Hel"),
            new(ChatRole.Assistant, "lo") { ModelId = "gpt-x-stream" },
            new ChatResponseUpdate
            {
                Contents = { new UsageContent(new UsageDetails { InputTokenCount = 50, OutputTokenCount = 20 }) }
            }
        };

        var collected = new List<ChatResponseUpdate>();
        await foreach (var u in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken))
            collected.Add(u);

        collected.Should().HaveCount(3);
        var snap = store.GetSnapshot();
        snap.TotalCalls.Should().Be(1);
        snap.TotalInputTokens.Should().Be(50);
        snap.TotalOutputTokens.Should().Be(20);
        snap.TotalCostUsd.Should().Be(0.0042m);
        snap.ByModel.Single().Model.Should().Be("gpt-x-stream");
    }

    // ─── Fake inner client ───
    private sealed class FakeChatClient : IChatClient
    {
        public ChatResponse Response { get; set; } = new(new ChatMessage(ChatRole.Assistant, ""));
        public List<ChatResponseUpdate> StreamingUpdates { get; set; } = new();
        public Exception? ThrowOnCall { get; set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (ThrowOnCall != null) throw ThrowOnCall;
            return Task.FromResult(Response);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (ThrowOnCall != null) throw ThrowOnCall;
            foreach (var u in StreamingUpdates)
            {
                yield return u;
                await Task.Yield();
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}

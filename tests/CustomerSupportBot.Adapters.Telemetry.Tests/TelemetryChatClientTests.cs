using CustomerSupportBot.Adapters.Telemetry.Chat;
using CustomerSupportBot.Adapters.Telemetry.OpenTelemetry;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using ChatResponse = Microsoft.Extensions.AI.ChatResponse;

namespace CustomerSupportBot.Adapters.Telemetry.Tests;

public class TelemetryChatClientTests
{
    private static (TelemetryChatClient Client, CostUsageStore Store, FakeChatClient Inner) Build(
        decimal estimatedCost = 0.01m)
    {
        var inner = new FakeChatClient();
        var calculator = Substitute.For<ICostCalculatorPort>();
        calculator.CalculateCost(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>()).Returns(estimatedCost);
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

    /// <summary>
    /// Görüşme başına maliyet: çağrı, o anda etkin görüşmeye (AsyncLocal kapsam) atfedilir ve kalıcı
    /// kayda oturum kimliğiyle yazılır. Kapsam dışındaki çağrılar (arka plan işleri) oturumsuz kalır.
    /// </summary>
    [Fact]
    public async Task PersistedRecord_CarriesTheConversationInScope()
    {
        var inner = new FakeChatClient
        {
            Response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
            {
                Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 }
            }
        };
        var calculator = Substitute.For<ICostCalculatorPort>();
        calculator.CalculateCost(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>()).Returns(0.002m);
        var records = new System.Collections.Concurrent.ConcurrentQueue<LlmCallRecord>();
        var persistence = Substitute.For<ILlmCallPersistencePort>();
        persistence.RecordAsync(Arg.Do<LlmCallRecord>(records.Enqueue), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var attribution = new CustomerSupportBot.Application.Services.Telemetry.LlmCallAttribution();
        var client = new TelemetryChatClient(inner, calculator, new CostUsageStore(), "gpt-x", "OpenAI",
            NullLogger<TelemetryChatClient>.Instance, persistence, attribution);
        var ct = TestContext.Current.CancellationToken;

        using (attribution.BeginSession("sess-1"))
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: ct);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "arka plan")], cancellationToken: ct);

        for (var i = 0; i < 50 && records.Count < 2; i++) await Task.Delay(20, ct);
        records.Select(r => r.SessionId).Should().BeEquivalentTo(["sess-1", null]);
    }
}

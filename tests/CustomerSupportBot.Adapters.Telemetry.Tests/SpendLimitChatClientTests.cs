// Harcama limiti sarmalayıcısı: aşımda çağrı yapılmaz; maliyet (yanıt ve akış) görüşmeyle kaydedilir.

using CustomerSupportBot.Adapters.Telemetry.Chat;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using Microsoft.Extensions.AI;
using ChatResponse = Microsoft.Extensions.AI.ChatResponse;

namespace CustomerSupportBot.Adapters.Telemetry.Tests;

public class SpendLimitChatClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class CountingChatClient : IChatClient
    {
        public int Calls { get; private set; }
        public UsageDetails Usage { get; set; } = new() { InputTokenCount = 100, OutputTokenCount = 50 };

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")) { ModelId = "gpt-real", Usage = Usage });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls++;
            yield return new ChatResponseUpdate(ChatRole.Assistant, "o") { ModelId = "gpt-real" };
            await Task.Yield();
            yield return new ChatResponseUpdate { Contents = [new UsageContent(Usage)] };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private static (SpendLimitChatClient Client, CountingChatClient Inner, ILlmSpendGuard Guard, ICostCalculatorPort Calculator) Build(
        LlmBudgetExceeded? exceeded = null, string? sessionId = "s1")
    {
        var inner = new CountingChatClient();
        var guard = Substitute.For<ILlmSpendGuard>();
        guard.CheckAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(exceeded);
        var calculator = Substitute.For<ICostCalculatorPort>();
        calculator.CalculateCost("gpt-real", "OpenAI", 100, 50).Returns(0.02m);
        var attribution = Substitute.For<ILlmCallAttribution>();
        attribution.CurrentSessionId.Returns(sessionId);
        return (new SpendLimitChatClient(inner, guard, calculator, "gpt-hint", "OpenAI", attribution), inner, guard, calculator);
    }

    [Fact]
    public async Task OverBudget_ThrowsBeforeCallingTheModel()
    {
        var (client, inner, _, _) = Build(new LlmBudgetExceeded(LlmBudgetScope.Daily, 5m, 5.1m));

        var act = () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: Ct);

        (await act.Should().ThrowAsync<LlmBudgetExceededException>()).Which.Detail.Scope.Should().Be(LlmBudgetScope.Daily);
        inner.Calls.Should().Be(0);
    }

    [Fact]
    public async Task OverBudget_Streaming_ThrowsBeforeCallingTheModel()
    {
        var (client, inner, _, _) = Build(new LlmBudgetExceeded(LlmBudgetScope.Conversation, 1m, 1m));

        var act = async () =>
        {
            await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: Ct)) { }
        };

        await act.Should().ThrowAsync<LlmBudgetExceededException>();
        inner.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Response_RecordsTheCost_ForTheConversationInScope()
    {
        var (client, _, guard, _) = Build();

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: Ct);

        await guard.Received(1).CheckAsync("s1", Arg.Any<CancellationToken>());
        await guard.Received(1).RecordAsync(0.02m, "s1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stream_RecordsTheCost_AfterTheLastUpdate()
    {
        var (client, _, guard, _) = Build(sessionId: null);

        var updates = 0;
        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: Ct))
            updates++;

        updates.Should().Be(2);
        await guard.Received(1).RecordAsync(0.02m, null, Arg.Any<CancellationToken>());
    }
}

// İstek, o anki model katmanına göre standart ya da hızlı istemciye gider; hızlı yoksa standart kullanılır.

using CustomerSupportBot.Adapters.AI;
using CustomerSupportBot.Adapters.AI.Chat;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Services.Chat;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Adapters.AI.Tests;

public class ModelRoutingChatClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Named(string name) : IChatClient
    {
        public int Calls { get; private set; }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, name)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            Calls++;
            yield return new ChatResponseUpdate(ChatRole.Assistant, name);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private static readonly ChatMessage[] Msgs = [new(ChatRole.User, "x")];

    [Fact]
    public async Task TheCurrentTier_PicksTheClient()
    {
        var tier = new AsyncLocalTurnModelTier();
        var std = new Named("standard"); var fast = new Named("fast");
        var client = new ModelRoutingChatClient(std, fast, tier);

        (await client.GetResponseAsync(Msgs, null, Ct)).Text.Should().Be("standard");
        using (tier.Begin(ModelTier.Fast))
            (await client.GetResponseAsync(Msgs, null, Ct)).Text.Should().Be("fast");
        (await client.GetResponseAsync(Msgs, null, Ct)).Text.Should().Be("standard");
    }

    [Fact]
    public async Task Streaming_IsRoutedToo()
    {
        var tier = new AsyncLocalTurnModelTier();
        var client = new ModelRoutingChatClient(new Named("standard"), new Named("fast"), tier);

        using var _ = tier.Begin(ModelTier.Fast);
        var text = "";
        await foreach (var u in client.GetStreamingResponseAsync(Msgs, null, Ct)) text += u.Text;

        text.Should().Be("fast");
    }

    [Fact]
    public async Task WithoutAFastClient_StandardServesEverything()
    {
        var tier = new AsyncLocalTurnModelTier();
        var std = new Named("standard");
        var client = new ModelRoutingChatClient(std, fast: null, tier);

        using var _ = tier.Begin(ModelTier.Fast);
        (await client.GetResponseAsync(Msgs, null, Ct)).Text.Should().Be("standard");
    }

    [Fact]
    public void TheFastClient_IsOnlyBuiltWhenConfigured()
    {
        var none = new AiOptions { Provider = AiProvider.OpenAI, OpenAI = { ApiKey = "sk-stub", Model = "gpt-x" } };
        var some = new AiOptions { Provider = AiProvider.OpenAI, OpenAI = { ApiKey = "sk-stub", Model = "gpt-x", FastModel = "gpt-mini" } };

        AiClientFactory.CreateFastChatClient(none).Should().BeNull();
        AiClientFactory.CreateFastChatClient(some).Should().NotBeNull();
    }
}

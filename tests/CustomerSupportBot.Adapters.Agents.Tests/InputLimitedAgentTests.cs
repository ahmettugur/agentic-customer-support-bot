// Tests/InputLimitedAgentTests.cs
//
// Dış kanaldan gelen girdinin boyut/karmaşıklık sınırı.
//
// Partner başına dakikalık istek sınırı "kaç kez" sorusunu sınırlar, "ne kadar" sorusunu
// değil. Hakkı olan istek sayısını çok büyük metinlerle kullanan bir çağıran token maliyetini
// ve çağrı süresini serbestçe büyütebilir. Bu testlerin asıl iddiası şu: sınır aşıldığında
// LLM'e HİÇ GİDİLMEZ — maliyet oluştuktan sonra tespit etmenin faydası yoktur.

using CustomerSupportBot.Adapters.Agents.A2A;
using CustomerSupportBot.Application.Services.A2A;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Adapters.Agents.Tests;

public class InputLimitedAgentTests
{
    /// <summary>
    /// Kaç kez çağrıldığını sayan sahte LLM istemcisi.
    ///
    /// <para>
    /// Ölçülen şey bilinçli olarak <b>iç ajanın</b> değil <b>LLM'in</b> çağrılıp çağrılmadığı:
    /// bu sınırın varlık sebebi token maliyeti ve çağrı süresidir.
    /// </para>
    /// </summary>
    private sealed class CountingChatClient : IChatClient
    {
        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "gercek yanit")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls++;
            yield return new ChatResponseUpdate(ChatRole.Assistant, "gercek yanit");
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private static (InputLimitedAgent Agent, CountingChatClient Llm) Build(
        int maxChars = 100, int maxParts = 3)
    {
        var llm = new CountingChatClient();
        var inner = new ChatClientAgent(llm, new ChatClientAgentOptions { Name = "test-agent" });
        var opts = new A2AOptions { MaxMessageChars = maxChars, MaxParts = maxParts };
        return (new InputLimitedAgent(inner, opts), llm);
    }

    private static ChatMessage Text(string s) => new(ChatRole.User, s);

    [Fact]
    public async Task Run_WithOversizedInput_DoesNotReachTheInnerAgent()
    {
        var (agent, llm) = Build(maxChars: 100);
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        var response = await agent.RunAsync(
            [Text(new string('x', 5000))], session,
            cancellationToken: TestContext.Current.CancellationToken);

        llm.Calls.Should().Be(0,
            "sınır LLM'e GİTMEDEN önce uygulanmalı; maliyet oluştuktan sonra reddetmenin faydası yok");
        response.Text.Should().Contain("çok uzun", "çağıran neyin yanlış olduğunu anlamalı");
        response.Text.Should().Contain("100", "izin verilen sınır söylenmeli");
    }

    [Fact]
    public async Task Run_WithTooManyParts_DoesNotReachTheInnerAgent()
    {
        // Uzunluk sınırını çok sayıda KÜÇÜK parçaya bölerek dolaşma girişimi.
        var (agent, llm) = Build(maxChars: 100, maxParts: 3);
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        var many = Enumerable.Range(0, 10).Select(i => Text($"p{i}")).ToArray();

        var response = await agent.RunAsync(many, session,
            cancellationToken: TestContext.Current.CancellationToken);

        llm.Calls.Should().Be(0);
        response.Text.Should().Contain("parça");
    }

    [Fact]
    public async Task Streaming_WithOversizedInput_DoesNotReachTheInnerAgent()
    {
        // Streaming AYRI bir kod yoludur; sınırın yalnızca non-streaming yolda uygulanması
        // çağıranın transport seçerek kontrolü atlamasına izin verirdi.
        var (agent, llm) = Build(maxChars: 100);
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        var updates = new List<AgentResponseUpdate>();
        await foreach (var u in agent.RunStreamingAsync(
            [Text(new string('x', 5000))], session,
            cancellationToken: TestContext.Current.CancellationToken))
        {
            updates.Add(u);
        }

        llm.Calls.Should().Be(0, "streaming yolunda da sınır uygulanmalı");
        string.Concat(updates.Select(u => u.Text)).Should().Contain("çok uzun");
    }

    // ─── Bulgu 2.3: DataContent karakter bütçesine katılmalı ────────────────────

    [Fact]
    public async Task Run_WithOversizedDataContent_DoesNotReachTheInnerAgent()
    {
        // Eskiden yalnızca TextContent sayılıyordu — büyük bir base64 yükü (DataContent)
        // MaxMessageChars'ı tamamen baypas edip LLM'e ulaşabiliyordu.
        var (agent, llm) = Build(maxChars: 100);
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        var bigPayload = new byte[500]; // data: URI'ye çevrilince kesinlikle >100 karakter eder
        var message = new ChatMessage(ChatRole.User, [new DataContent(bigPayload, "application/octet-stream")]);

        var response = await agent.RunAsync(
            [message], session, cancellationToken: TestContext.Current.CancellationToken);

        llm.Calls.Should().Be(0,
            "büyük bir DataContent, karakter bütçesini TextContent kadar aşabilmeli");
        response.Text.Should().Contain("çok uzun");
    }

    [Fact]
    public async Task Run_WithSmallDataContent_PassesThrough()
    {
        var (agent, llm) = Build(maxChars: 10_000);
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        var smallPayload = new byte[8];
        var message = new ChatMessage(ChatRole.User, [new DataContent(smallPayload, "image/png")]);

        var response = await agent.RunAsync(
            [message], session, cancellationToken: TestContext.Current.CancellationToken);

        llm.Calls.Should().Be(1, "küçük bir ek meşru kullanımı engellememeli");
    }

    [Fact]
    public async Task Run_WithNormalInput_PassesThrough()
    {
        // Karşı yön: sınır, olağan kullanımı engellememeli.
        var (agent, llm) = Build(maxChars: 100);
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        var response = await agent.RunAsync(
            [Text("Cay fiyati nedir?")], session,
            cancellationToken: TestContext.Current.CancellationToken);

        llm.Calls.Should().Be(1);
        response.Text.Should().Contain("gercek yanit");
    }
}

using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Services.Reasoning;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class ReasoningServiceCancellationTests
{
    private sealed class CancellingReasoningClient : IReasoningChatClient
    {
        public string ModelName => "test";
        public string ReasoningEffort => "low";

        public Task<string> CompleteAsync(
            IReadOnlyList<ConversationMessage> messages,
            CancellationToken ct = default) => Task.FromCanceled<string>(ct);

        public async IAsyncEnumerable<string> StreamAsync(
            IReadOnlyList<ConversationMessage> messages,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            yield break;
        }
    }

    private static ReasoningService Build(int timeoutSeconds = 45, IReasoningChatClient? client = null)
    {
        var prompts = Substitute.For<IPromptRepository>();
        prompts.Render(Arg.Any<string>(), Arg.Any<IDictionary<string, string?>?>())
            .Returns("system prompt");
        prompts.Get(Arg.Any<string>()).Returns("");

        return new ReasoningService(
            client ?? new CancellingReasoningClient(),
            NullLogger<ReasoningService>.Instance,
            prompts,
            new EntityVerifier(NullLogger<EntityVerifier>.Instance),
            new ReasoningSanityChecker(NullLogger<ReasoningSanityChecker>.Instance),
            Options.Create(new WorkflowGuardOptions { ReasoningTimeoutSeconds = timeoutSeconds }));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    public async Task NonObjectModelOutput_StillCompletesReasoningWithFallback(string json)
    {
        static async IAsyncEnumerable<string> Output(string text)
        {
            await Task.Yield();
            yield return text;
        }
        var client = Substitute.For<IReasoningChatClient>();
        client.StreamAsync(Arg.Any<IReadOnlyList<ConversationMessage>>(), Arg.Any<CancellationToken>()).Returns(Output(json));
        var events = new List<StreamEvent>();
        await foreach (var evt in Build(client: client).ReasonStreamingAsync("query", new AgentSession(),
            ct: TestContext.Current.CancellationToken)) events.Add(evt);
        events.Last().Type.Should().Be(StreamEventTypes.ReasoningComplete);
        events.Last().Data.Should().BeOfType<ReasoningResult>().Which.IsFallback.Should().BeTrue();
    }

    [Fact]
    public async Task ReasonAsync_CallerCancellation_IsNotConvertedToFallback()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => Build().ReasonAsync(
            "merhaba", new AgentSession(), null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ReasonStreamingAsync_CallerCancellation_IsNotConvertedToIncompleteResult()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        async Task EnumerateAsync()
        {
            await foreach (var _ in Build().ReasonStreamingAsync(
                "merhaba", new AgentSession(), null, cts.Token))
            {
            }
        }

        Func<Task> act = EnumerateAsync;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }


    /// <summary>
    /// STREAMING reasoning'in de bir bütçesi olmalı.
    ///
    /// <para>
    /// Timeout ilk eklendiğinde yalnızca non-streaming <c>ReasonAsync</c>'e konmuştu — oysa
    /// asıl arayüz <c>/chat/stream</c> kullanıyor, yani pratikte KORUNMAYAN yol buydu:
    /// asılı kalan bir reasoning akışı hiçbir sınıra tabi değildi ve tur süresiz bekleyebiliyordu.
    /// </para>
    ///
    /// <para>
    /// Süre dolduğunda tur ÖLMEZ: fallback reasoning ile devam eder (niyet çıkarımı kaybolur,
    /// kullanıcı yanıtsız kalmaz).
    /// </para>
    /// </summary>
    [Fact]
    public async Task ReasonStreamingAsync_WhenModelHangs_FallsBackWithinBudget()
    {
        var sut = Build(timeoutSeconds: 1);
        var session = new AgentSession { SessionId = "s1" };

        var events = new List<StreamEvent>();
        var started = DateTimeOffset.UtcNow;

        await foreach (var e in sut.ReasonStreamingAsync("soru", session, null, CancellationToken.None))
            events.Add(e);

        var elapsed = DateTimeOffset.UtcNow - started;
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20),
            "asılı model çağrısı bütçeyle kesilmeli; aksi hâlde tur süresiz bekler");

        var complete = events.Last(e => e.Type == StreamEventTypes.ReasoningComplete);
        var result = complete.Data.Should().BeOfType<ReasoningResult>().Subject;
        result.Confidence.Should().Be(WellKnown.Confidence.Low, "timeout fallback'e düşmeli");

        // Bulgu 2.5: tüketici IsFallback'e BAKMADAN, yalnızca düşük Confidence'a bakarak
        // "reasoning gerçekten çalıştı ama emin değildi" ile "reasoning hiç çalışmadı"
        // durumlarını ayırt edemiyordu.
        result.IsFallback.Should().BeTrue(
            "timeout sonucu üretilen ReasoningResult açıkça fallback olarak işaretlenmeli");
    }

    /// <summary>
    /// Bulgu 2.5'in non-streaming (ReasonAsync) karşılığı — aynı ayrım burada da geçerli.
    /// </summary>
    private sealed class ThrowingReasoningClient : IReasoningChatClient
    {
        public string ModelName => "test";
        public string ReasoningEffort => "low";

        public Task<string> CompleteAsync(
            IReadOnlyList<ConversationMessage> messages,
            CancellationToken ct = default) => throw new InvalidOperationException("simulated LLM failure");

        public IAsyncEnumerable<string> StreamAsync(
            IReadOnlyList<ConversationMessage> messages,
            CancellationToken ct = default) => throw new NotImplementedException();
    }

    [Fact]
    public async Task ReasonAsync_WhenClientThrows_ReturnsResultMarkedAsFallback()
    {
        var prompts = Substitute.For<IPromptRepository>();
        prompts.Render(Arg.Any<string>(), Arg.Any<IDictionary<string, string?>?>())
            .Returns("system prompt");
        prompts.Get(Arg.Any<string>()).Returns("");

        var sut = new ReasoningService(
            new ThrowingReasoningClient(),
            NullLogger<ReasoningService>.Instance,
            prompts,
            new EntityVerifier(NullLogger<EntityVerifier>.Instance),
            new ReasoningSanityChecker(NullLogger<ReasoningSanityChecker>.Instance),
            Options.Create(new WorkflowGuardOptions { ReasoningTimeoutSeconds = 45 }));

        var result = await sut.ReasonAsync("soru", new AgentSession { SessionId = "s1" });

        result.Confidence.Should().Be(WellKnown.Confidence.Low);
        result.IsFallback.Should().BeTrue(
            "istisna sonucu üretilen ReasoningResult açıkça fallback olarak işaretlenmeli");
    }
}

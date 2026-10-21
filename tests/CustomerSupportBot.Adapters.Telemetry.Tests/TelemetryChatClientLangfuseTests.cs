// LLM span'i Langfuse'un "generation" olarak tanıyacağı biçimde: gen_ai.* semantic conventions, maliyet,
// oturum/kullanıcı ve (yalnızca açıkça istenirse, PII maskelenmiş ve kısaltılmış) prompt/yanıt içeriği.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using CustomerSupportBot.Adapters.Telemetry.Chat;
using CustomerSupportBot.Adapters.Telemetry.OpenTelemetry;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using ChatResponse = Microsoft.Extensions.AI.ChatResponse;

namespace CustomerSupportBot.Adapters.Telemetry.Tests;

public sealed class TelemetryChatClientLangfuseTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Testler paralel koşar: her test kendi model ipucuyla yalnızca kendi span'ini toplar.
    private readonly string _model = "gpt-lf-" + Guid.NewGuid().ToString("N")[..8];
    private readonly ConcurrentBag<Activity> _stopped = new();
    private readonly ActivityListener _listener;

    public TelemetryChatClientLangfuseTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == CustomerSupportTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a =>
            {
                if (Equals(a.GetTagItem("gen_ai.request.model"), _model) || Equals(a.GetTagItem("ai.model"), _model))
                    _stopped.Add(a);
            }
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    private Activity Span => _stopped.Should().ContainSingle().Subject;

    private TelemetryChatClient Build(FakeChatClient inner, LlmTraceEnrichment? enrichment = null, string? sessionId = null)
    {
        var calculator = Substitute.For<ICostCalculatorPort>();
        calculator.CalculateCost(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>()).Returns(0.0042m);
        var attribution = Substitute.For<ILlmCallAttribution>();
        attribution.CurrentSessionId.Returns(sessionId);
        return new TelemetryChatClient(inner, calculator, new CostUsageStore(), _model, "OpenAI",
            NullLogger<TelemetryChatClient>.Instance, attribution: attribution, enrichment: enrichment);
    }

    private static FakeChatClient Inner(string text = "Siparişiniz kargoda.") => new()
    {
        Response = new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
        {
            ModelId = "gpt-5.4-2026-08-01",
            Usage = new UsageDetails { InputTokenCount = 120, OutputTokenCount = 30 }
        }
    };

    [Fact]
    public async Task Span_CarriesGenAiConventions_CostAndGenerationType()
    {
        var client = Build(Inner(), sessionId: "sess-1");

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "kargom nerede")], cancellationToken: Ct);

        var s = Span;
        s.DisplayName.Should().Be($"chat {_model}");
        s.GetTagItem("gen_ai.operation.name").Should().Be("chat");
        s.GetTagItem("gen_ai.system").Should().Be("openai");
        s.GetTagItem("gen_ai.request.model").Should().Be(_model);
        s.GetTagItem("gen_ai.response.model").Should().Be("gpt-5.4-2026-08-01");
        s.GetTagItem("gen_ai.usage.input_tokens").Should().Be(120L);
        s.GetTagItem("gen_ai.usage.output_tokens").Should().Be(30L);
        s.GetTagItem("langfuse.observation.type").Should().Be("generation");
        JsonDocument.Parse((string)s.GetTagItem("langfuse.observation.cost_details")!).RootElement
            .GetProperty("total").GetDecimal().Should().Be(0.0042m, "Langfuse'un fiyat tablosunda olmayan modellerde de maliyet görünsün");
        s.GetTagItem("session.id").Should().Be("sess-1");
        s.GetTagItem("langfuse.session.id").Should().Be("sess-1");
    }

    [Fact]
    public async Task UserId_ComesFromTheEnrichmentContext()
    {
        var client = Build(Inner(), new LlmTraceEnrichment { CurrentUserId = () => "1027" });

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "x")], cancellationToken: Ct);

        Span.GetTagItem("user.id").Should().Be("1027");
        Span.GetTagItem("langfuse.user.id").Should().Be("1027");
    }

    [Fact]
    public async Task PromptVersion_IsStampedAsLangfuseVersion()
    {
        var client = Build(Inner(), new LlmTraceEnrichment { PromptVersion = "a1b2c3d4" });

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "x")], cancellationToken: Ct);

        Span.GetTagItem("langfuse.version").Should().Be("a1b2c3d4");
        Span.GetTagItem("prompt.version").Should().Be("a1b2c3d4");
    }

    [Fact]
    public async Task WithoutPromptVersion_NoVersionTagIsWritten()
    {
        var client = Build(Inner());

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "x")], cancellationToken: Ct);

        Span.GetTagItem("langfuse.version").Should().BeNull();
    }

    [Fact]
    public async Task ContentIsNotCaptured_ByDefault()
    {
        var client = Build(Inner());

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "kartım 4111 1111 1111 1111")], cancellationToken: Ct);

        Span.GetTagItem("langfuse.observation.input").Should().BeNull();
        Span.GetTagItem("langfuse.observation.output").Should().BeNull();
    }

    [Fact]
    public async Task CapturedContent_IsRedacted_AndKeepsRoles()
    {
        var client = Build(Inner("Kartınız ****1111 ile ödeme alındı. E-posta: ali@example.com"), new LlmTraceEnrichment
        {
            CaptureContent = true,
            Redact = s => s.Replace("ali@example.com", "a***@example.com").Replace("4111 1111 1111 1111", "**** **** **** 1111")
        });

        await client.GetResponseAsync(
        [
            new ChatMessage(ChatRole.System, "Sen bir destek asistanısın."),
            new ChatMessage(ChatRole.User, "kartım 4111 1111 1111 1111")
        ], cancellationToken: Ct);

        var input = JsonDocument.Parse((string)Span.GetTagItem("langfuse.observation.input")!).RootElement;
        input.GetArrayLength().Should().Be(2);
        input[0].GetProperty("role").GetString().Should().Be("system");
        input[1].GetProperty("role").GetString().Should().Be("user");
        input[1].GetProperty("content").GetString().Should().Be("kartım **** **** **** 1111");
        ((string)Span.GetTagItem("langfuse.observation.output")!).Should().Be("Kartınız ****1111 ile ödeme alındı. E-posta: a***@example.com");
    }

    [Fact]
    public async Task ToolCallsAndResults_AreSummarizedInTheInput()
    {
        var client = Build(Inner(), new LlmTraceEnrichment { CaptureContent = true });

        await client.GetResponseAsync(
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "order_status_tool", new Dictionary<string, object?> { ["orderId"] = "1030" })]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("c1", "Kargolandı")])
        ], cancellationToken: Ct);

        var input = (string)Span.GetTagItem("langfuse.observation.input")!;
        input.Should().Contain("order_status_tool").And.Contain("1030").And.Contain("Kargolandı");
    }

    [Fact]
    public async Task LongContent_IsTruncated()
    {
        var client = Build(Inner(new string('y', 5000)), new LlmTraceEnrichment { CaptureContent = true, MaxContentChars = 1000 });

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, new string('x', 5000))], cancellationToken: Ct);

        ((string)Span.GetTagItem("langfuse.observation.input")!).Length.Should().BeLessThanOrEqualTo(1000);
        ((string)Span.GetTagItem("langfuse.observation.output")!).Length.Should().BeLessThanOrEqualTo(1000);
    }

    [Fact]
    public async Task Streaming_CapturesTheAssembledOutput()
    {
        var inner = new FakeChatClient
        {
            StreamingUpdates =
            [
                new ChatResponseUpdate(ChatRole.Assistant, "Merhaba, "),
                new ChatResponseUpdate(ChatRole.Assistant, "siparişiniz yolda.") { ModelId = "gpt-5.4-2026-08-01" },
                new ChatResponseUpdate { Contents = [new UsageContent(new UsageDetails { InputTokenCount = 7, OutputTokenCount = 4 })] }
            ]
        };
        var client = Build(inner, new LlmTraceEnrichment { CaptureContent = true });

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "x")], cancellationToken: Ct)) { }

        Span.GetTagItem("langfuse.observation.output").Should().Be("Merhaba, siparişiniz yolda.");
        Span.GetTagItem("gen_ai.usage.output_tokens").Should().Be(4L);
    }

    private sealed class FakeChatClient : IChatClient
    {
        public ChatResponse Response { get; set; } = new(new ChatMessage(ChatRole.Assistant, ""));
        public List<ChatResponseUpdate> StreamingUpdates { get; set; } = new();

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Response);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
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

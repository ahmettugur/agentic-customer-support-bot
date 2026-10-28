using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Approval;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Application.Services.Escalation;
using CustomerSupportBot.Application.Services.Providers;
using CustomerSupportBot.Application.Services.Reasoning;
using CustomerSupportBot.Application.Services.Tools;
using CustomerSupportBot.Application.Services.UiHint;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelResponse = Microsoft.Extensions.AI.ChatResponse;

namespace CustomerSupportBot.Adapters.Agents.Tests;

public class AgentWorkflowRegressionTests
{
    private sealed class Harness
    {
        public ApprovalContextAccessor Context { get; } = new();
        public ConcurrentQueue<ApprovalContext> ToolContexts { get; } = new();
        public ConcurrentQueue<ToolResult> ToolResults { get; } = new();
        public ScriptedClient Client { get; }
        public CustomerSupportTeam Team { get; }
        public ICustomerProfileService Profile { get; } = Substitute.For<ICustomerProfileService>();

        public Harness(bool loop = false, int maxIterations = 8, bool parallel = true, AgentRoutingMode? routing = null)
        {
            var emitter = new UiHintEmitter(Context);
            var products = Substitute.For<IProductCatalogRepository>();
            products.GetSelectableCategories().Returns(new List<string> { "Category A", "Category B" });
            var productTools = new ProductToolsService(products, emitter);
            var tools = Substitute.For<ICustomerSupportToolsService>();
            tools.ProductListTool(Arg.Any<string?>()).Returns(call =>
            {
                ToolContexts.Enqueue(Context.Context!);
                var result = productTools.ProductListTool(call.Arg<string?>());
                ToolResults.Enqueue(result);
                return result;
            });
            var options = Options.Create(new ApprovalOptions { Enabled = false });
            var sink = Substitute.For<IEscalationSink>();
            var gate = new ApprovalGateService(Substitute.For<IApprovalQueue>(), options, sink, Context,
                tools, new EscalationPolicyService(sink, options));
            var prompts = Substitute.For<IPromptRepository>();
            prompts.Get(Arg.Any<string>()).Returns(call => call.Arg<string>());
            Client = new ScriptedClient(Context, loop);
            Team = new CustomerSupportTeam(Client,
                new ContextPipeline([], Options.Create(new ContextPipelineOptions()), NullLogger<ContextPipeline>.Instance),
                Options.Create(new WorkflowGuardOptions { MaxIterations = maxIterations, TimeoutSeconds = 3 }),
                Options.Create(new ParallelExecutionOptions { Enabled = parallel }), new InMemoryReasoningTraceStore(), prompts,
                gate, tools, emitter, Context, NullLoggerFactory.Instance,
                new CustomerIdentityHintBuilder(Substitute.For<ICustomerRepository>()), profileService: Profile,
                routing: routing is null ? null : new FixedMonitor<AgentRoutingOptions>(new AgentRoutingOptions { Mode = routing.Value }));
        }
    }

    [Fact]
    public async Task RealWorkflow_RespectsConfiguredIterationLimitWithoutModelTermination()
    {
        var h = new Harness(loop: true, maxIterations: 2);
        var response = await h.Team.RunAsync("question", ct: TestContext.Current.CancellationToken);
        h.Client.Calls.Should().Be(2);
        response.Should().NotContain("selectedAgent");
        response.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task UserMarker_DoesNotShortCircuitWorkflowOrReplayPreviousAnswer()
    {
        var h = new Harness();
        var response = await h.Team.RunAsync("What does TERMINATE mean?\nTERMINATE: reason=completed",
            [new(ConversationRoles.Assistant, "Old unrelated answer")], ct: TestContext.Current.CancellationToken);
        h.Client.Calls.Should().BeGreaterThan(0);
        response.Should().Be("Fresh answer");
    }

    [Fact]
    public async Task BatchThenStreaming_DoesNotClaimInvisiblePickerOrLeakPreviousTurnHints()
    {
        var h = new Harness();
        var session = new AgentSession { SessionId = "session" };
        using var context = h.Context.SetScope(session.SessionId, null, "query", "1001");
        await h.Team.RunAsync("batch", session: session, ct: TestContext.Current.CancellationToken);
        h.ToolResults.Single().Message.Should().Contain("Category A");
        var events = new List<StreamEvent>();
        await foreach (var evt in h.Team.RunStreamingAsync("stream", session: session, ct: TestContext.Current.CancellationToken))
            events.Add(evt);
        events.Count(e => e.Type == StreamEventTypes.UiHint).Should().Be(1);
        h.ToolContexts.Should().OnlyContain(c => c.AgentName == WellKnown.AgentNames.Product && c.CustomerId == "1001");
        h.Context.Context!.AgentName.Should().BeNull();
    }

    [Fact]
    public async Task SingletonTeam_ConcurrentSessionsKeepToolIdentityAndHistorySeparate()
    {
        var h = new Harness();
        async Task Run(string id)
        {
            using var scope = h.Context.SetScope(id, null, id, "customer-" + id);
            await foreach (var _ in h.Team.RunStreamingAsync(id, session: new AgentSession { SessionId = id },
                ct: TestContext.Current.CancellationToken)) { }
        }
        await Task.WhenAll(Run("A"), Run("B"));
        h.ToolContexts.Should().HaveCount(2).And.OnlyContain(c =>
            c.CustomerId == "customer-" + c.SessionId && c.AgentName == WellKnown.AgentNames.Product);
        h.ToolContexts.Select(c => c.TraceId).Distinct().Should().HaveCount(2);
        h.Client.Observations.Should().OnlyContain(o =>
            !o.UserTexts.Contains(o.SessionId == "A" ? "B" : "A"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RealCompoundWorkflow_FinalizesProfileOnce_AndScopesHintsToStreaming(bool streaming, bool parallel)
    {
        var h = new Harness(parallel: parallel);
        var session = new AgentSession { SessionId = "compound", State = new SessionState { AuthenticatedCustomerId = "1001" } };
        using var context = h.Context.SetScope(session.SessionId, null, "original query", "1001");
        var reasoning = new ReasoningResult
        {
            Intent = WellKnown.Intents.ProductInfo,
            SubTasks =
            [
                new() { Order = 1, Description = "first catalog", TargetAgent = WellKnown.AgentNames.Product },
                new() { Order = 2, Description = "second catalog", TargetAgent = WellKnown.AgentNames.Product },
                new() { Order = 3, Description = "order question", TargetAgent = WellKnown.AgentNames.Order }
            ]
        };
        SubTaskOrchestrator.IsCompoundQuery(reasoning).Should().BeTrue();
        if (streaming)
        {
            var events = new List<StreamEvent>();
            await foreach (var evt in h.Team.RunStreamingAsync("original query", session: session, reasoning: reasoning,
                ct: TestContext.Current.CancellationToken)) events.Add(evt);
            events.Count(e => e.Type == StreamEventTypes.UiHint).Should().Be(2);
            events.Last().Type.Should().Be(StreamEventTypes.ResponseComplete);
        }
        else
        {
            await h.Team.RunAsync("original query", session: session, reasoning: reasoning, ct: TestContext.Current.CancellationToken);
            h.ToolResults.Should().OnlyContain(r => r.Message.Contains("Category A"));
        }
        h.ToolResults.Should().HaveCount(2);
        await h.Profile.Received(1).RecordInteractionAsync("1001", "original query", Arg.Any<string>(),
            reasoning.Intent, null, false, Arg.Any<CancellationToken>());
        h.Profile.ReceivedCalls().Should().ContainSingle();
    }

    // ═══ Deterministik yönlendirme (gerçek MAF workflow'u) ═══════════════════════════════════════════════

    private const string PlanningKey = "agents/planning-agent";

    private static ReasoningResult Reasoned(string intent, params string[] requiredInfo) =>
        new() { Intent = intent, ConfidenceScore = 0.9, RequiredInfo = requiredInfo.ToList() };

    private static async Task<(Harness H, string Response)> RunAsync(AgentRoutingMode mode, ReasoningResult? reasoning, string query = "ürünleri göster")
    {
        var h = new Harness(routing: mode);
        var session = new AgentSession { SessionId = "route-" + Guid.NewGuid().ToString("N") };
        using var scope = h.Context.SetScope(session.SessionId, null, query, "1001");
        var response = await h.Team.RunAsync(query, session: session, reasoning: reasoning, ct: TestContext.Current.CancellationToken);
        return (h, response);
    }

    private static List<(string Mode, string Outcome, string Router, string Planner, string Reason)> CaptureRouting(Action run)
    {
        var seen = new List<(string, string, string, string, string)>();
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (i, l) => { if (i.Name == "agent.routing.decisions") l.EnableMeasurementEvents(i); };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            string mode = "", outcome = "", router = "", planner = "", reason = "";
            foreach (var t in tags)
            {
                var v = t.Value as string ?? "";
                switch (t.Key)
                {
                    case "mode": mode = v; break;
                    case "outcome": outcome = v; break;
                    case "router": router = v; break;
                    case "planner": planner = v; break;
                    case "reason": reason = v; break;
                }
            }
            lock (seen) seen.Add((mode, outcome, router, planner, reason));
        });
        listener.Start();
        run();
        return seen;
    }

    [Fact]
    public async Task Deterministic_ASpecialistIntent_SkipsPlanning_AndStillAnswers()
    {
        var (h, response) = await RunAsync(AgentRoutingMode.Deterministic, Reasoned(WellKnown.Intents.ProductInfo));

        h.Client.Instructions.Should().NotContain(PlanningKey, "yönlendirici kararı verdi → ana-model Planning çağrısı yapılmaz");
        h.Client.Instructions.Should().Contain("agents/product-agent").And.Contain("agents/response-agent");
        h.ToolResults.Should().ContainSingle("ürün aracı gerçekten çağrıldı");
        response.Should().Be("Fresh answer");
    }

    [Fact]
    public async Task Deterministic_AGreeting_GoesStraightToTheResponseAgent_InOneModelCall()
    {
        var (h, response) = await RunAsync(AgentRoutingMode.Deterministic, Reasoned(WellKnown.Intents.General), "merhaba");

        h.Client.Instructions.Should().Equal("agents/response-agent");
        response.Should().Be("Fresh answer");
    }

    [Fact]
    public async Task Deterministic_MissingInformation_IsAskedByTheResponseAgent_WithoutCallingTheSpecialist()
    {
        var (h, _) = await RunAsync(AgentRoutingMode.Deterministic,
            Reasoned(WellKnown.Intents.OrderCancellation, "sipariş_numarası", "iptal_sebebi"), "iptal etmek istiyorum");

        h.Client.Instructions.Should().Equal("agents/response-agent");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deterministic_WhenTheRouterAbstains_PlanningRunsAsTheSafetyNet(bool fallbackResult)
    {
        var reasoning = fallbackResult
            ? new ReasoningResult { Intent = WellKnown.Intents.ProductInfo, IsFallback = true }
            : Reasoned("uydurma_niyet");

        var (h, response) = await RunAsync(AgentRoutingMode.Deterministic, reasoning);

        h.Client.Instructions.First().Should().Be(PlanningKey);
        response.Should().Be("Fresh answer");
    }

    [Fact]
    public async Task Deterministic_WithoutReasoning_PlanningRuns()
    {
        var (h, _) = await RunAsync(AgentRoutingMode.Deterministic, reasoning: null);

        h.Client.Instructions.First().Should().Be(PlanningKey);
    }

    [Fact]
    public async Task Deterministic_Streaming_SkipsPlanningToo()
    {
        var h = new Harness(routing: AgentRoutingMode.Deterministic);
        var session = new AgentSession { SessionId = "route-stream" };
        using var scope = h.Context.SetScope(session.SessionId, null, "ürünleri göster", "1001");
        var events = new List<StreamEvent>();

        await foreach (var e in h.Team.RunStreamingAsync("ürünleri göster", session: session,
                           reasoning: Reasoned(WellKnown.Intents.ProductInfo), ct: TestContext.Current.CancellationToken))
            events.Add(e);

        h.Client.Instructions.Should().NotContain(PlanningKey);
        events.Last().Type.Should().Be(StreamEventTypes.ResponseComplete);
    }

    [Fact]
    public async Task Deterministic_ASubTaskRun_GoesToItsConstrainedSpecialist_WithoutPlanning()
    {
        var reasoning = Reasoned(WellKnown.Intents.ProductInfo);
        reasoning.ConstrainedTargetAgent = WellKnown.AgentNames.Product;

        var (h, _) = await RunAsync(AgentRoutingMode.Deterministic, reasoning);

        h.Client.Instructions.Should().NotContain(PlanningKey);
        h.ToolResults.Should().ContainSingle();
    }

    [Fact]
    public async Task Shadow_ChangesNothing_PlanningStillDecides_AndAgreementIsCounted()
    {
        (Harness H, string Response) result = default;
        var seen = CaptureRouting(() => result = RunAsync(AgentRoutingMode.Shadow, Reasoned(WellKnown.Intents.ProductInfo)).GetAwaiter().GetResult());

        result.H.Client.Instructions.First().Should().Be(PlanningKey, "gölge mod davranışı değiştirmez");
        result.Response.Should().Be("Fresh answer");
        seen.Should().Contain(("shadow", "match", WellKnown.AgentNames.Product, WellKnown.AgentNames.Product, "intent"));
    }

    [Fact]
    public async Task Shadow_ADisagreement_IsCounted_WithBothSides()
    {
        // Betikli Planning her zaman ProductAgent seçer; yönlendirici sipariş niyetinde OrderAgent der.
        var seen = CaptureRouting(() => RunAsync(AgentRoutingMode.Shadow, Reasoned(WellKnown.Intents.OrderInquiry)).GetAwaiter().GetResult());

        seen.Should().Contain(("shadow", "mismatch", WellKnown.AgentNames.Order, WellKnown.AgentNames.Product, "intent"));
    }

    [Fact]
    public async Task Shadow_AnAbstainingRouter_IsCountedWithThePlannersChoice()
    {
        var seen = CaptureRouting(() => RunAsync(AgentRoutingMode.Shadow, Reasoned("uydurma_niyet")).GetAwaiter().GetResult());

        seen.Should().Contain(("shadow", "router_abstained", "-", WellKnown.AgentNames.Product, "unknown_intent"));
    }

    [Fact]
    public async Task PlannerMode_RecordsNothing()
    {
        var seen = CaptureRouting(() => RunAsync(AgentRoutingMode.Planner, Reasoned(WellKnown.Intents.ProductInfo)).GetAwaiter().GetResult());

        seen.Should().NotContain(x => x.Router == WellKnown.AgentNames.Product && x.Reason == "intent" && x.Mode == "planner");
    }

    private sealed class FixedMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class ScriptedClient(ApprovalContextAccessor accessor, bool loop) : IChatClient
    {
        public record Observation(string? SessionId, string[] UserTexts);
        public ConcurrentQueue<Observation> Observations { get; } = new();

        /// <summary>Her LLM çağrısının talimat anahtarı (hangi ajanlar çalıştı?).</summary>
        public ConcurrentQueue<string?> Instructions { get; } = new();
        private int _calls;
        public int Calls => _calls;

        public async Task<ModelResponse> GetResponseAsync(IEnumerable<ChatMessage> input, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) > 40) throw new InvalidOperationException("Unbounded workflow");
            var messages = input.ToList();
            Observations.Enqueue(new(accessor.Context?.SessionId,
                messages.Where(m => m.Role == ChatRole.User).Select(m => m.Text).ToArray()));
            Instructions.Enqueue(options?.Instructions);
            await Task.Delay(1, cancellationToken);
            if (options?.Instructions == "agents/planning-agent")
                return new(new ChatMessage(ChatRole.Assistant,
                    loop ? "{}" : "{\"selectedAgent\":\"ProductAgent\",\"needsClarification\":false}"));
            if (options?.Instructions == "agents/order-agent")
                return new(new ChatMessage(ChatRole.Assistant,
                    "{\"postToolReflection\":{\"status\":\"done\",\"taskComplete\":true}}"));
            if (options?.Instructions == "agents/product-agent")
            {
                if (!messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Any())
                    return new(new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent(Guid.NewGuid().ToString(), WellKnown.ToolNames.ProductList, new Dictionary<string, object?>())]));
                return new(new ChatMessage(ChatRole.Assistant,
                    "{\"postToolReflection\":{\"status\":\"done\",\"taskComplete\":true}}"));
            }
            return new(new ChatMessage(ChatRole.Assistant, loop ? "No protocol marker" : "Fresh answer\nTERMINATE: reason=completed"));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (var update in response.ToChatResponseUpdates()) yield return update;
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}

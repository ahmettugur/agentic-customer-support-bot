// ChatPortService reasoning akışını müşteriye göndermeden önce süzer: ham JSON parçaları yerine yalnızca analiz metni,
// reasoning_complete yerine müşteri görünümü; /chat yanıtındaki reasoning de aynı görünüm.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class ReasoningPrivacyChatTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string RawJson =
        "{\"analysis\":\"Kullanıcı siparişini soruyor, mail: baska@example.com\",\"nextAction\":\"OrderAgent'e yönlendir GİZLİ\"," +
        "\"rationale\":\"GİZLİ-RATIONALE\"}";

    private static ReasoningResult Internal() => new()
    {
        Analysis = "Kullanıcı siparişini soruyor, mail: baska@example.com",
        Intent = WellKnown.Intents.OrderInquiry, ConfidenceScore = 0.9, Confidence = "yüksek",
        NextAction = "OrderAgent'e yönlendir GİZLİ", Rationale = "GİZLİ-RATIONALE", DecisionReason = "GİZLİ-DECISION",
        SanityIssues = [new ReasoningIssue { Code = "GİZLİ-SANITY", Message = "GİZLİ" }],
        Steps = [new ReasoningStep { Order = 1, Description = "sorgula", Premise = "GİZLİ-PREMISE" }]
    };

    private sealed class Reasoning : IReasoningPort
    {
        public Task<ReasoningResult> ReasonAsync(string q, AgentSession? s = null, List<ConversationMessage>? h = null, CancellationToken ct = default)
            => Task.FromResult(Internal());

        public async IAsyncEnumerable<StreamEvent> ReasonStreamingAsync(string q, AgentSession? s = null, List<ConversationMessage>? h = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new StreamEvent(StreamEventTypes.ReasoningStart, null);
            for (var i = 0; i < RawJson.Length; i += 9)
                yield return new StreamEvent(StreamEventTypes.ReasoningDelta, new TextDeltaPayload(RawJson.Substring(i, Math.Min(9, RawJson.Length - i))));
            yield return new StreamEvent(StreamEventTypes.ReasoningComplete, Internal());
            await Task.CompletedTask;
        }
    }

    private sealed class Team : IAgentTeamPort
    {
        public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(string q, List<ConversationMessage>? h = null, AgentSession? s = null,
            ReasoningResult? r = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new StreamEvent(StreamEventTypes.ResponseComplete, new ResponseCompletePayload("Siparişiniz yolda.", WellKnown.Termination.ReasonCompleted));
            await Task.CompletedTask;
        }
        public Task<string> RunAsync(string q, List<ConversationMessage>? h = null, AgentSession? s = null, ReasoningResult? r = null,
            CancellationToken ct = default) => Task.FromResult("Siparişiniz yolda.");
        public string GetWorkflowDiagram() => "";
    }

    private static ChatPortService Build()
    {
        var customers = Substitute.For<ICustomerRepository>();
        var guardrail = new OutputGuardrailService(customers, Options.Create(new OutputGuardrailOptions()),
            Options.Create(new EmailOptions()), NullLogger<OutputGuardrailService>.Instance);
        var sessions = new InMemorySessionManager(new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 5 })));
        var modes = Substitute.For<IChatModeRegistry>();
        modes.GetMode(Arg.Any<string>()).Returns(ChatMode.Bot);
        return new ChatPortService(new Team(), new Reasoning(), sessions, modes, Substitute.For<IChatBridge>(),
            new SessionStateService(sessions, NullLogger<SessionStateService>.Instance), Substitute.For<IApprovalContextAccessor>(),
            new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 5 })),
            NullLogger<ChatPortService>.Instance, guardrail: guardrail);
    }

    private static async Task<List<StreamEvent>> StreamAsync(ChatPortService chat)
    {
        var events = new List<StreamEvent>();
        await foreach (var e in chat.HandleStreamAsync(new ChatRequest("siparişim nerede", "s-priv"), Ct)) events.Add(e);
        return events;
    }

    private static string Wire(IEnumerable<StreamEvent> events) =>
        string.Join("\n", events.Where(e => e.Type.StartsWith("reasoning", StringComparison.Ordinal)).Select(e =>
            e.Type + ":" + System.Text.Json.JsonSerializer.Serialize(e.Data, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
            { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })));

    [Fact]
    public async Task TheStream_CarriesOnlyThePlainAnalysisText_NotTheRawJson()
    {
        var events = await StreamAsync(Build());

        var deltas = events.Where(e => e.Type == StreamEventTypes.ReasoningDelta).Select(e => ((TextDeltaPayload)e.Data!).Text).ToList();
        string.Concat(deltas).Should().StartWith("Kullanıcı siparişini soruyor");
        string.Concat(deltas).Should().NotContain("{").And.NotContain("nextAction").And.NotContain("GİZLİ");
    }

    [Fact]
    public async Task TheCompleteEvent_IsTheCustomerView_WithNoInternals()
    {
        var events = await StreamAsync(Build());

        events.Single(e => e.Type == StreamEventTypes.ReasoningComplete).Data.Should().BeOfType<CustomerReasoningView>();
        Wire(events).Should().NotContain("GİZLİ").And.NotContain("nextAction").And.NotContain("sanityIssues").And.NotContain("premise");
    }

    [Fact]
    public async Task AForeignContactInTheAnalysis_IsMasked_InTheCompleteEvent()
    {
        var events = await StreamAsync(Build());

        var view = (CustomerReasoningView)events.Single(e => e.Type == StreamEventTypes.ReasoningComplete).Data!;

        view.Analysis.Should().NotContain("baska@example.com").And.Contain(OutputGuardrail.RedactedMark);
    }

    [Fact]
    public async Task TheNonStreamingResponse_AlsoCarriesTheView()
    {
        var response = await Build().HandleAsync(new ChatRequest("siparişim nerede", "s-priv-2"), Ct);

        response.Reasoning.Should().NotBeNull();
        response.Reasoning!.Analysis.Should().NotContain("baska@example.com");
        System.Text.Json.JsonSerializer.Serialize(response).Should().NotContain("GİZLİ");
    }

    [Fact]
    public async Task TheInternalResult_StillDrivesTheTurn_NotTheView()
    {
        // Yönlendirme/niyet kararları (düşük güven sayacı, model katmanı…) iç ReasoningResult'tan okunur; süzme yalnızca ağ çıktısını değiştirir.
        var chat = Build();
        var events = await StreamAsync(chat);

        events.Any(e => e.Type == StreamEventTypes.ResponseComplete).Should().BeTrue();
    }
}

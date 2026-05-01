using CustomerSupportBot.Agents.Routing;
using CustomerSupportBot.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Tests.Agents.Routing;

public class RoutingStrategyTests
{
    private readonly AIAgent _planning = Substitute.For<AIAgent>();
    private readonly AIAgent _response = Substitute.For<AIAgent>();
    private readonly AIAgent _orderInquiry = Substitute.For<AIAgent>();
    private readonly AIAgent _complaint = Substitute.For<AIAgent>();

    private RoutingContext BuildCtx() => new()
    {
        AgentsByName = new Dictionary<string, AIAgent>(StringComparer.OrdinalIgnoreCase)
        {
            [WellKnown.AgentNames.Planning] = _planning,
            [WellKnown.AgentNames.Response] = _response,
            [WellKnown.AgentNames.OrderInquiry] = _orderInquiry,
            [WellKnown.AgentNames.Complaint] = _complaint
        },
        PlanningAgent = _planning,
        ResponseAgent = _response,
        Guards = new WorkflowGuardOptions(),
        ChatClient = Substitute.For<IChatClient>(),
        SelectionSystemPrompt = "stub",
        Logger = NullLogger.Instance
    };

    // ─── FirstTurnStrategy ───
    [Fact]
    public async Task FirstTurn_NoPlanningInHistory_ReturnsPlanning()
    {
        var ctx = BuildCtx();
        var s = new FirstTurnStrategy(ctx);
        var history = new List<ChatMessage> { new(ChatRole.User, "merhaba") };
        var result = await s.TrySelectAsync(history, history[0], default);
        result.Should().NotBeNull();
        result!.Value.Agent.Should().Be(_planning);
        result.Value.Branch.Should().Be(Branches.FirstTurn);
    }

    [Fact]
    public async Task FirstTurn_PlanningAlreadySpoke_ReturnsNull()
    {
        var ctx = BuildCtx();
        var s = new FirstTurnStrategy(ctx);
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "merhaba"),
            new(ChatRole.Assistant, "{}") { AuthorName = WellKnown.AgentNames.Planning }
        };
        var result = await s.TrySelectAsync(history, history[1], default);
        result.Should().BeNull();
    }

    // ─── PlanRoutingStrategy ───
    [Fact]
    public async Task Plan_LastMessageNotPlanning_ReturnsNull()
    {
        var ctx = BuildCtx();
        var s = new PlanRoutingStrategy(ctx);
        var history = new List<ChatMessage> { new(ChatRole.User, "x") };
        var result = await s.TrySelectAsync(history, history[0], default);
        result.Should().BeNull();
    }

    [Fact]
    public async Task Plan_ParseFailed_FallsBackToResponse()
    {
        var ctx = BuildCtx();
        var s = new PlanRoutingStrategy(ctx);
        var msg = new ChatMessage(ChatRole.Assistant, "not_json")
        {
            AuthorName = WellKnown.AgentNames.Planning
        };
        var result = await s.TrySelectAsync(new[] { msg }, msg, default);
        result!.Value.Agent.Should().Be(_response);
        result.Value.Branch.Should().Be(Branches.PlanParseFailed);
    }

    [Fact]
    public async Task Plan_NeedsClarification_ReturnsResponse()
    {
        var ctx = BuildCtx();
        var s = new PlanRoutingStrategy(ctx);
        var json = """{"intent":"general","selectedAgent":"OrderInquiryAgent","intentConfidence":0.9,"needsClarification":true}""";
        var msg = new ChatMessage(ChatRole.Assistant, json) { AuthorName = WellKnown.AgentNames.Planning };
        var result = await s.TrySelectAsync(new[] { msg }, msg, default);
        result!.Value.Branch.Should().Be(Branches.PlanClarification);
    }

    [Fact]
    public async Task Plan_LowConfidence_ReturnsClarification()
    {
        var ctx = BuildCtx();
        var s = new PlanRoutingStrategy(ctx);
        var json = """{"intent":"general","selectedAgent":"OrderInquiryAgent","intentConfidence":0.1,"needsClarification":false}""";
        var msg = new ChatMessage(ChatRole.Assistant, json) { AuthorName = WellKnown.AgentNames.Planning };
        var result = await s.TrySelectAsync(new[] { msg }, msg, default);
        result!.Value.Branch.Should().Be(Branches.PlanClarification);
    }

    [Fact]
    public async Task Plan_UnknownAgent_FallsBackToResponse()
    {
        var ctx = BuildCtx();
        var s = new PlanRoutingStrategy(ctx);
        var json = """{"intent":"x","selectedAgent":"NoSuchAgent","intentConfidence":0.9,"needsClarification":false}""";
        var msg = new ChatMessage(ChatRole.Assistant, json) { AuthorName = WellKnown.AgentNames.Planning };
        var result = await s.TrySelectAsync(new[] { msg }, msg, default);
        result!.Value.Branch.Should().Be(Branches.PlanUnknownAgent);
    }

    [Fact]
    public async Task Plan_KnownAgent_ReturnsThatAgent()
    {
        var ctx = BuildCtx();
        var s = new PlanRoutingStrategy(ctx);
        var json = "{\"intent\":\"order_inquiry\",\"selectedAgent\":\"" + WellKnown.AgentNames.OrderInquiry + "\",\"intentConfidence\":0.95,\"needsClarification\":false}";
        var msg = new ChatMessage(ChatRole.Assistant, json) { AuthorName = WellKnown.AgentNames.Planning };
        var result = await s.TrySelectAsync(new[] { msg }, msg, default);
        result!.Value.Agent.Should().Be(_orderInquiry);
        result.Value.Branch.Should().Be(Branches.Plan);
    }

    // ─── ReflectionRoutingStrategy ───
    [Fact]
    public async Task Reflection_NotSpecialist_ReturnsNull()
    {
        var ctx = BuildCtx();
        var s = new ReflectionRoutingStrategy(ctx);
        var msg = new ChatMessage(ChatRole.User, "merhaba");
        var result = await s.TrySelectAsync(new[] { msg }, msg, default);
        result.Should().BeNull();
    }

    [Fact]
    public async Task Reflection_NoReflection_FallsBackToResponse()
    {
        var ctx = BuildCtx();
        var s = new ReflectionRoutingStrategy(ctx);
        var msg = new ChatMessage(ChatRole.Assistant, "düz cevap")
        {
            AuthorName = WellKnown.AgentNames.OrderInquiry
        };
        var result = await s.TrySelectAsync(new[] { msg }, msg, default);
        result!.Value.Agent.Should().Be(_response);
        result.Value.Branch.Should().Be(Branches.ReflectionMissing);
    }

    [Fact]
    public async Task Reflection_NeedsEscalation_ReturnsResponse()
    {
        var ctx = BuildCtx();
        var s = new ReflectionRoutingStrategy(ctx);
        var json = """{"reasoning":"x","postToolReflection":{"status":"needs_escalation","summary":"y","handoffReason":"z"}}""";
        var msg = new ChatMessage(ChatRole.Assistant, json)
        {
            AuthorName = WellKnown.AgentNames.Complaint
        };
        var result = await s.TrySelectAsync(new[] { msg }, msg, default);
        result!.Value.Branch.Should().Be(Branches.ReflectionEscalation);
    }

    [Fact]
    public async Task Reflection_HandoffToKnownAgent_ReturnsThatAgent()
    {
        var ctx = BuildCtx();
        var s = new ReflectionRoutingStrategy(ctx);
        var json = "{\"reasoning\":\"x\",\"postToolReflection\":{\"status\":\"completed\",\"summary\":\"y\",\"handoffSuggestion\":\"" + WellKnown.AgentNames.Complaint + "\"}}";
        var msg = new ChatMessage(ChatRole.Assistant, json)
        {
            AuthorName = WellKnown.AgentNames.OrderInquiry
        };
        var result = await s.TrySelectAsync(new[] { msg }, msg, default);
        result!.Value.Agent.Should().Be(_complaint);
        result.Value.Branch.Should().Be(Branches.ReflectionHandoff);
    }

    [Fact]
    public async Task Reflection_Completed_ReturnsResponse()
    {
        var ctx = BuildCtx();
        var s = new ReflectionRoutingStrategy(ctx);
        var json = """{"reasoning":"x","postToolReflection":{"status":"completed","summary":"y"}}""";
        var msg = new ChatMessage(ChatRole.Assistant, json)
        {
            AuthorName = WellKnown.AgentNames.Complaint
        };
        var result = await s.TrySelectAsync(new[] { msg }, msg, default);
        result!.Value.Agent.Should().Be(_response);
        result.Value.Branch.Should().Be(Branches.ReflectionComplete);
    }

    // ─── RoutingContext helpers ───
    [Fact]
    public void IsSpecialistMessage_NullAuthor_False()
    {
        var msg = new ChatMessage(ChatRole.User, "x");
        RoutingContext.IsSpecialistMessage(msg).Should().BeFalse();
    }

    [Fact]
    public void IsSpecialistMessage_SpecialistPrefix_True()
    {
        var msg = new ChatMessage(ChatRole.Assistant, "x")
        {
            AuthorName = WellKnown.AgentNames.OrderInquiry
        };
        RoutingContext.IsSpecialistMessage(msg).Should().BeTrue();
    }

    [Fact]
    public void Resolve_UnknownName_ReturnsNull()
    {
        var ctx = BuildCtx();
        ctx.Resolve("NotExisting").Should().BeNull();
        ctx.Resolve(null).Should().BeNull();
        ctx.Resolve("").Should().BeNull();
    }

    [Fact]
    public void Resolve_KnownName_ReturnsAgent()
    {
        var ctx = BuildCtx();
        ctx.Resolve(WellKnown.AgentNames.Planning).Should().Be(_planning);
    }
}

using System.Reflection;
using CustomerSupportBot.Api.Agents;
using CustomerSupportBot.Api.Models;
using CustomerSupportBot.Api.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Api.Tests.Agents;

public class CustomerSupportChatManagerTests
{
    private readonly PromptService _prompts = new(NullLogger<PromptService>.Instance);

    private static AIAgent MakeAgent(string name, string desc = "stub")
    {
        var agent = Substitute.For<AIAgent>();
        agent.Name.Returns(name);
        agent.Description.Returns(desc);
        return agent;
    }

    private CustomerSupportChatManager BuildManager(WorkflowGuardOptions? guards = null, params AIAgent[] extra)
    {
        var planning = MakeAgent(WellKnown.AgentNames.Planning);
        var response = MakeAgent(WellKnown.AgentNames.Response);
        var orderInquiry = MakeAgent(WellKnown.AgentNames.OrderInquiry);
        var complaint = MakeAgent(WellKnown.AgentNames.Complaint);
        var agents = new List<AIAgent> { planning, response, orderInquiry, complaint };
        agents.AddRange(extra);
        return new CustomerSupportChatManager(
            agents,
            guards ?? new WorkflowGuardOptions(),
            NullLogger<CustomerSupportChatManager>.Instance);
    }

    [Fact]
    public void Constructor_MissingPlanning_Throws()
    {
        var agents = new List<AIAgent> { MakeAgent(WellKnown.AgentNames.Response) };
        Action act = () => new CustomerSupportChatManager(
            agents,
            new WorkflowGuardOptions(),
            NullLogger<CustomerSupportChatManager>.Instance);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Planning*");
    }

    [Fact]
    public void Constructor_MissingResponse_Throws()
    {
        var agents = new List<AIAgent> { MakeAgent(WellKnown.AgentNames.Planning) };
        Action act = () => new CustomerSupportChatManager(
            agents,
            new WorkflowGuardOptions(),
            NullLogger<CustomerSupportChatManager>.Instance);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Response*");
    }

    [Fact]
    public void Constructor_AllRequired_Builds()
    {
        var mgr = BuildManager();
        mgr.Should().NotBeNull();
    }

    // ─── SelectNextAgentAsync via reflection (protected) ───
    private static async ValueTask<AIAgent> InvokeSelectAsync(
        CustomerSupportChatManager mgr, IReadOnlyList<ChatMessage> history)
    {
        var method = typeof(CustomerSupportChatManager).GetMethod(
            "SelectNextAgentAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (ValueTask<AIAgent>)method.Invoke(mgr, new object[] { history, default(CancellationToken) })!;
        return await task;
    }

    [Fact]
    public async Task SelectNextAgent_FirstTurn_PicksPlanning()
    {
        var mgr = BuildManager();
        var history = new List<ChatMessage> { new(ChatRole.User, "merhaba") };
        var picked = await InvokeSelectAsync(mgr, history);
        picked.Name.Should().Be(WellKnown.AgentNames.Planning);
    }

    [Fact]
    public async Task SelectNextAgent_AfterPlan_RoutesToSelectedSpecialist()
    {
        var mgr = BuildManager();
        var planJson = "{\"intent\":\"order_inquiry\",\"selectedAgent\":\"" +
                       WellKnown.AgentNames.OrderInquiry +
                       "\",\"intentConfidence\":0.95,\"needsClarification\":false}";
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "siparişimi sor"),
            new(ChatRole.Assistant, planJson) { AuthorName = WellKnown.AgentNames.Planning }
        };
        var picked = await InvokeSelectAsync(mgr, history);
        picked.Name.Should().Be(WellKnown.AgentNames.OrderInquiry);
    }

    [Fact]
    public async Task SelectNextAgent_HandoffLimitExceeded_FallsBackToResponse()
    {
        var mgr = BuildManager(new WorkflowGuardOptions { MaxHandoffsPerAgent = 1 });
        var planJson = "{\"intent\":\"order_inquiry\",\"selectedAgent\":\"" +
                       WellKnown.AgentNames.OrderInquiry +
                       "\",\"intentConfidence\":0.95,\"needsClarification\":false}";
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "soru"),
            new(ChatRole.Assistant, planJson) { AuthorName = WellKnown.AgentNames.Planning }
        };
        // İlk seçim limit içinde, ikinci seçim limit dışı kalmalı → ResponseAgent'a düşer
        var first = await InvokeSelectAsync(mgr, history);
        first.Name.Should().Be(WellKnown.AgentNames.OrderInquiry);

        var second = await InvokeSelectAsync(mgr, history);
        second.Name.Should().Be(WellKnown.AgentNames.Response);
    }

    // ─── ShouldTerminateAsync ───
    private static async ValueTask<bool> InvokeShouldTerminate(
        CustomerSupportChatManager mgr, IReadOnlyList<ChatMessage> history)
    {
        var method = typeof(CustomerSupportChatManager).GetMethod(
            "ShouldTerminateAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (ValueTask<bool>)method.Invoke(mgr, new object[] { history, default(CancellationToken) })!;
        return await task;
    }

    [Fact]
    public async Task ShouldTerminate_TerminateMarker_ReturnsTrue()
    {
        var mgr = BuildManager();
        var history = new List<ChatMessage>
        {
            new(ChatRole.Assistant, "yanıt hazır TERMINATE")
        };
        (await InvokeShouldTerminate(mgr, history)).Should().BeTrue();
    }

    [Fact]
    public async Task ShouldTerminate_NoMarker_ReturnsFalse()
    {
        var mgr = BuildManager();
        var history = new List<ChatMessage> { new(ChatRole.Assistant, "merhaba") };
        (await InvokeShouldTerminate(mgr, history)).Should().BeFalse();
    }

    [Fact]
    public async Task ShouldTerminate_RepeatedToolCalls_ReturnsTrue()
    {
        var mgr = BuildManager(new WorkflowGuardOptions { MaxDuplicateToolCalls = 2 });
        // Aynı tool çağrısını iki kere içeren history
        var fc = new FunctionCallContent("call1", "tool_x", new Dictionary<string, object?> { ["a"] = 1 });
        var fc2 = new FunctionCallContent("call2", "tool_x", new Dictionary<string, object?> { ["a"] = 1 });
        var msg1 = new ChatMessage(ChatRole.Assistant, new[] { (AIContent)fc });
        var msg2 = new ChatMessage(ChatRole.Assistant, new[] { (AIContent)fc2 });
        var history = new List<ChatMessage> { msg1, msg2 };
        (await InvokeShouldTerminate(mgr, history)).Should().BeTrue();
    }
}

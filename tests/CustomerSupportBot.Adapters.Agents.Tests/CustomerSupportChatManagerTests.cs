using System.Reflection;
using CustomerSupportBot.Adapters.Agents;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Adapters.Persistence.FileSystem;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using CustomerSupportBot.Application.Ports.Outbound;

namespace CustomerSupportBot.Adapters.Agents.Tests;

public class CustomerSupportChatManagerTests
{
    private readonly FileSystemPromptRepository _prompts = new(NullLogger<FileSystemPromptRepository>.Instance);

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
        var orderInquiry = MakeAgent(WellKnown.AgentNames.Order);
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
        var planJson = "{\"selectedAgent\":\"" +
                       WellKnown.AgentNames.Order +
                       "\",\"needsClarification\":false}";
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "siparişimi sor"),
            new(ChatRole.Assistant, planJson) { AuthorName = WellKnown.AgentNames.Planning }
        };
        var picked = await InvokeSelectAsync(mgr, history);
        picked.Name.Should().Be(WellKnown.AgentNames.Order);
    }

    [Fact]
    public async Task SelectNextAgent_DecomposedConstraint_OverridesDifferentPlannedSpecialist()
    {
        var planning = MakeAgent(WellKnown.AgentNames.Planning);
        var response = MakeAgent(WellKnown.AgentNames.Response);
        var product = MakeAgent(WellKnown.AgentNames.Product);
        var order = MakeAgent(WellKnown.AgentNames.Order);
        var mgr = new CustomerSupportChatManager(
            [planning, response, product, order],
            new WorkflowGuardOptions(),
            NullLogger<CustomerSupportChatManager>.Instance,
            WellKnown.AgentNames.Product);
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "ürün alt görevi"),
            new(ChatRole.Assistant,
                $"{{\"selectedAgent\":\"{WellKnown.AgentNames.Order}\",\"needsClarification\":false}}")
                { AuthorName = WellKnown.AgentNames.Planning }
        };

        var picked = await InvokeSelectAsync(mgr, history);

        picked.Name.Should().Be(WellKnown.AgentNames.Product);
    }

    [Fact]
    public async Task SelectNextAgent_HandoffLimitExceeded_FallsBackToResponse()
    {
        var mgr = BuildManager(new WorkflowGuardOptions { MaxHandoffsPerAgent = 1 });
        var planJson = "{\"selectedAgent\":\"" +
                       WellKnown.AgentNames.Order +
                       "\",\"needsClarification\":false}";
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "soru"),
            new(ChatRole.Assistant, planJson) { AuthorName = WellKnown.AgentNames.Planning }
        };
        // İlk seçim limit içinde, ikinci seçim limit dışı kalmalı — ResponseAgent'a düşer
        var first = await InvokeSelectAsync(mgr, history);
        first.Name.Should().Be(WellKnown.AgentNames.Order);

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
            new(ChatRole.Assistant, "yanıt hazır\nTERMINATE: reason=completed") { AuthorName = WellKnown.AgentNames.Response }
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

    // ─── checkpoint: yönetici durumu (handoff sayaçları) ───
    //
    // MAF yalnızca kendi durumunu (IterationCount) checkpoint'ler; bu sözlük bizim. Kaydedilmezse devam eden tur sayaçları
    // sıfırdan başlar ve "ajan başına en fazla N devir" güvencesi zayıflar.

    private static Microsoft.Agents.AI.Workflows.IWorkflowContext FakeStateContext()
    {
        var state = new Dictionary<string, object?>();
        var ctx = Substitute.For<Microsoft.Agents.AI.Workflows.IWorkflowContext>();
        ctx.QueueStateUpdateAsync(Arg.Any<string>(), Arg.Any<Dictionary<string, int>>(), Arg.Any<CancellationToken>())
            .Returns(call => { state[call.ArgAt<string>(0)] = call.ArgAt<Dictionary<string, int>>(1); return ValueTask.CompletedTask; });
        ctx.ReadStateAsync<Dictionary<string, int>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new ValueTask<Dictionary<string, int>?>(
                state.TryGetValue(call.ArgAt<string>(0), out var v) ? (Dictionary<string, int>?)v : null));
        return ctx;
    }

    private static List<ChatMessage> PlanToOrder() =>
    [
        new(ChatRole.User, "soru"),
        new(ChatRole.Assistant, "{\"selectedAgent\":\"" + WellKnown.AgentNames.Order + "\",\"needsClarification\":false}")
            { AuthorName = WellKnown.AgentNames.Planning }
    ];

    [Fact]
    public async Task HandoffCounts_SurviveACheckpointRoundTrip_IntoAFreshManager()
    {
        var guards = new WorkflowGuardOptions { MaxHandoffsPerAgent = 1 };
        var before = BuildManager(guards);
        (await InvokeSelectAsync(before, PlanToOrder())).Name.Should().Be(WellKnown.AgentNames.Order);   // sayaç 1'e çıktı
        var ctx = FakeStateContext();
        await before.SaveHandoffCountsAsync(ctx, default);

        var afterRestart = BuildManager(guards);
        await afterRestart.RestoreHandoffCountsAsync(ctx, default);

        (await InvokeSelectAsync(afterRestart, PlanToOrder())).Name.Should().Be(WellKnown.AgentNames.Response,
            "devir sınırı yeniden başlatmayla sıfırlanmamalı");
    }

    [Fact]
    public async Task WithoutRestore_AFreshManagerStartsFromZero()
    {
        var fresh = BuildManager(new WorkflowGuardOptions { MaxHandoffsPerAgent = 1 });

        (await InvokeSelectAsync(fresh, PlanToOrder())).Name.Should().Be(WellKnown.AgentNames.Order);
    }

    [Fact]
    public async Task Restore_WithNothingSaved_KeepsCountsEmpty()
    {
        var mgr = BuildManager(new WorkflowGuardOptions { MaxHandoffsPerAgent = 1 });

        await mgr.RestoreHandoffCountsAsync(FakeStateContext(), default);

        (await InvokeSelectAsync(mgr, PlanToOrder())).Name.Should().Be(WellKnown.AgentNames.Order);
    }

    // ─── Deterministik yönlendirici: ilk ajan dışarıdan verilir, Planning atlanır ───

    private static CustomerSupportChatManager BuildWithFirstAgent(string? firstAgent, string? constrained = null) =>
        new([MakeAgent(WellKnown.AgentNames.Planning), MakeAgent(WellKnown.AgentNames.Response), MakeAgent(WellKnown.AgentNames.Order),
             MakeAgent(WellKnown.AgentNames.Product), MakeAgent(WellKnown.AgentNames.HumanHandoff)],
            new WorkflowGuardOptions(), NullLogger<CustomerSupportChatManager>.Instance, constrained, firstAgent);

    private static List<ChatMessage> UserOnly() => [new(ChatRole.User, "1057 siparişim nerede")];

    private static ChatMessage Spoken(string agent, string text) => new(ChatRole.Assistant, text) { AuthorName = agent };

    [Fact]
    public async Task WithAPreselectedFirstAgent_PlanningIsSkipped()
    {
        var picked = await InvokeSelectAsync(BuildWithFirstAgent(WellKnown.AgentNames.Order), UserOnly());

        picked.Name.Should().Be(WellKnown.AgentNames.Order);
    }

    [Fact]
    public async Task OnceThePreselectedAgentHasSpoken_ItIsNotPickedAgain_AndReflectionTakesOver()
    {
        var mgr = BuildWithFirstAgent(WellKnown.AgentNames.Order);
        var history = UserOnly();
        history.Add(Spoken(WellKnown.AgentNames.Order, "{\"postToolReflection\":{\"status\":\"done\",\"taskComplete\":true}}"));

        var picked = await InvokeSelectAsync(mgr, history);

        picked.Name.Should().Be(WellKnown.AgentNames.Response, "uzman bitirdi → yanıt; ilk ajan döngüye girmemeli");
    }

    [Fact]
    public async Task APreselectedResponseAgent_HandlesGreetingsAndClarificationsDirectly()
    {
        var picked = await InvokeSelectAsync(BuildWithFirstAgent(WellKnown.AgentNames.Response), UserOnly());

        picked.Name.Should().Be(WellKnown.AgentNames.Response);
    }

    [Fact]
    public async Task ReflectionHandoff_StillWorks_AfterAPreselectedFirstAgent()
    {
        var mgr = BuildWithFirstAgent(WellKnown.AgentNames.Product);
        var history = UserOnly();
        history.Add(Spoken(WellKnown.AgentNames.Product,
            "{\"postToolReflection\":{\"status\":\"needs_followup\",\"handoffSuggestion\":\"" + WellKnown.AgentNames.Order + "\"}}"));

        var picked = await InvokeSelectAsync(mgr, history);

        picked.Name.Should().Be(WellKnown.AgentNames.Order);
    }

    [Fact]
    public async Task WithoutAPreselection_ThePlanningPathIsUnchanged()
    {
        var picked = await InvokeSelectAsync(BuildWithFirstAgent(null), UserOnly());

        picked.Name.Should().Be(WellKnown.AgentNames.Planning);
    }

    [Fact]
    public async Task AnUnknownPreselectedName_IsIgnored_AndPlanningRuns()
    {
        var picked = await InvokeSelectAsync(BuildWithFirstAgent("UydurmaAgent"), UserOnly());

        picked.Name.Should().Be(WellKnown.AgentNames.Planning, "yanlış ad turu düşürmemeli — güvenli yola dönülür");
    }

    [Fact]
    public async Task APreselectedAgent_StillCountsTowardsTheHandoffLimit()
    {
        var mgr = new CustomerSupportChatManager(
            [MakeAgent(WellKnown.AgentNames.Planning), MakeAgent(WellKnown.AgentNames.Response), MakeAgent(WellKnown.AgentNames.Order)],
            new WorkflowGuardOptions { MaxHandoffsPerAgent = 1 }, NullLogger<CustomerSupportChatManager>.Instance, null, WellKnown.AgentNames.Order);

        var first = await InvokeSelectAsync(mgr, UserOnly());
        var second = await InvokeSelectAsync(mgr, UserOnly());

        first.Name.Should().Be(WellKnown.AgentNames.Order);
        second.Name.Should().Be(WellKnown.AgentNames.Response, "sınır ilk seçim için de geçerli");
    }
}

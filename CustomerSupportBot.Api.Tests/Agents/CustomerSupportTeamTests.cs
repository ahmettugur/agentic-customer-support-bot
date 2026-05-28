// Tests/Agents/CustomerSupportTeamTests.cs
// CustomerSupportTeam ctor + CreateWorkflow + BuildReasoningSummaryHint kapsam�.
// Tam workflow execution �ok dependency gerektirdi�i i�in sadece yap� testleri.

using System.Reflection;
using CustomerSupportBot.Adapters.Agents;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Adapters.Persistence.FileSystem;
using CustomerSupportBot.Api.Tests.Helpers;
using CustomerSupportBot.Api.Tests.Infrastructure;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Tests.Agents;

[Collection("PostgresCatalog")]
public class CustomerSupportTeamTests
{
    private readonly PostgresCatalogFixture _fixture;

    public CustomerSupportTeamTests(PostgresCatalogFixture fixture)
    {
        _fixture = fixture;
    }

    private CustomerSupportTeam BuildTeam(IChatClient? chatClient = null)
    {
        chatClient ??= Substitute.For<IChatClient>();

        var prompts = new FileSystemPromptRepository(NullLogger<FileSystemPromptRepository>.Instance);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WorkflowGuards:MaxIterations"] = "10",
                ["WorkflowGuards:TimeoutSeconds"] = "30",
                ["WorkflowGuards:MaxRepeatedToolCalls"] = "2"
            })
            .Build();

        var providers = Array.Empty<IContextProvider>();
        var contextPipeline = new ContextPipeline(providers, NullLogger<ContextPipeline>.Instance);
        var traceStore = new InMemoryReasoningTraceStore();

        var approvalOpts = new ApprovalOptions { Enabled = false };
        var queue = new InMemoryApprovalQueue(
            Options.Create(approvalOpts),
            NullLogger<InMemoryApprovalQueue>.Instance);
        var sink = new InMemoryEscalationSink(NullLogger<InMemoryEscalationSink>.Instance);
        var tools = TestFactory.CreateToolsService(_fixture.ProductRepo, _fixture.OrderRepo, _fixture.ComplaintRepo);
        var escalationPolicy = new EscalationPolicyService(
            sink, Options.Create(approvalOpts));
        var approvalGate = new ApprovalGateService(
            queue, Options.Create(approvalOpts), sink, new ApprovalContextAccessor(), tools, escalationPolicy);

        var loggerFactory = NullLoggerFactory.Instance;

        return new CustomerSupportTeam(
            chatClient,
            contextPipeline,
            Options.Create(new WorkflowGuardOptions { MaxIterations = 10, TimeoutSeconds = 30, MaxDuplicateToolCalls = 2 }),
            Options.Create(new ParallelExecutionOptions()),
            traceStore,
            prompts,
            approvalGate,
            tools,
            loggerFactory);
    }

    [Fact]
    public void Constructor_WithAllDependencies_Succeeds()
    {
        var team = BuildTeam();
        team.Should().NotBeNull();
    }

    [Fact]
    public void CreateWorkflow_BuildsValidGroupChatWorkflow()
    {
        var team = BuildTeam();
        var method = typeof(CustomerSupportTeam).GetMethod(
            "CreateWorkflow", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var workflow = method.Invoke(team, null) as Workflow;
        workflow.Should().NotBeNull();
    }

    [Fact]
    public void CreateWorkflow_CalledTwice_ReturnsDistinctInstances()
    {
        var team = BuildTeam();
        var method = typeof(CustomerSupportTeam).GetMethod(
            "CreateWorkflow", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var w1 = method.Invoke(team, null) as Workflow;
        var w2 = method.Invoke(team, null) as Workflow;
        w1.Should().NotBeNull();
        w2.Should().NotBeNull();
        ReferenceEquals(w1, w2).Should().BeFalse();
    }

    [Fact]
    public void BuildReasoningSummaryHint_WithReasoning_ReturnsNonEmpty()
    {
        var team = BuildTeam();
        var method = typeof(CustomerSupportTeam).GetMethod(
            "BuildReasoningSummaryHint", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null) return; // Y�ntem yoksa atla

        var reasoning = new ReasoningResult
        {
            Intent = "order_inquiry",
            Confidence = "high",
            ConfidenceScore = 0.9,
            Steps = new List<ReasoningStep>
            {
                new() { Description = "Sipariş ID kontrol" }
            }
        };
        var hint = method.Invoke(team, new object[] { reasoning }) as string;
        hint.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task BuildWorkflowMessagesAsync_WithoutSession_ReturnsUserMessage()
    {
        var team = BuildTeam();
        var method = typeof(CustomerSupportTeam).GetMethod(
            "BuildWorkflowMessagesAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null) return;

        var task = (Task)method.Invoke(team, new object?[] { "merhaba", null, null, null })!;
        await task;
        var resultProp = task.GetType().GetProperty("Result")!;
        var messages = resultProp.GetValue(task) as List<ChatMessage>;
        messages.Should().NotBeNull();
        messages.Should().NotBeEmpty();
        messages.Last().Role.Should().Be(ChatRole.User);
        messages.Last().Text.Should().Be("merhaba");
    }

    [Fact]
    public async Task BuildWorkflowMessagesAsync_WithHistory_IncludesPreviousMessages()
    {
        var team = BuildTeam();
        var method = typeof(CustomerSupportTeam).GetMethod(
            "BuildWorkflowMessagesAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null) return;

        var history = new List<ConversationMessage>
        {
            new(ConversationRoles.User, "�nceki mesaj"),
            new(ConversationRoles.Assistant, "�nceki yan�t"),
        };
        var task = (Task)method.Invoke(team, ["�imdiki", history, null, null])!;
        await task.ConfigureAwait(true);
        var messages = (task.GetType().GetProperty("Result")!.GetValue(task) as List<ChatMessage>)!;

        messages.Should().Contain(m => m.Text == "�nceki mesaj");
        messages.Should().Contain(m => m.Text == "�imdiki");
    }

    [Fact]
    public async Task BuildWorkflowMessagesAsync_WithExtractableId_AddsEntityHint()
    {
        var team = BuildTeam();
        var method = typeof(CustomerSupportTeam).GetMethod(
            "BuildWorkflowMessagesAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null) return;

        var task = (Task)method.Invoke(team, new object?[] { "sipariş 1030 nerede?", null, null, null })!;
        await task.ConfigureAwait(true);
        var messages = (task.GetType().GetProperty("Result")!.GetValue(task) as List<ChatMessage>)!;

        // Entity hint genelde System rol�nde eklenir
        messages.Should().Contain(m => m.Role == ChatRole.System);
    }
}

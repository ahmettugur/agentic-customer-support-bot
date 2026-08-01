// Tests/Agents/CustomerSupportTeamTests.cs
// CustomerSupportTeam ctor + kompozisyon kökü olarak doğru koşuculara sahip olduğunu test eder.
// AgentTeamFactory/WorkflowRunner/TurnFinalizer/DecomposedRunner ayrı sınıflara bölündüğü için
// (bkz. dosyaların kendisi) CreateWorkflow/BuildReasoningSummaryHint/BuildWorkflowMessagesAsync/
// ResponseStreamFilter testleri artık ilgili sınıfı hedefliyor — InternalsVisibleTo sayesinde
// doğrudan örneklenebiliyorlar, reflection sadece private metodlara erişim için gerekiyor.

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

    private sealed record Deps(
        IChatClient ChatClient,
        FileSystemPromptRepository Prompts,
        ContextPipeline ContextPipeline,
        InMemoryReasoningTraceStore TraceStore,
        ApprovalGateService ApprovalGate,
        ICustomerSupportToolsService Tools,
        WorkflowGuardOptions Guards,
        CustomerSupportBot.Application.Services.UiHint.UiHintEmitter UiHint,
        ApprovalContextAccessor ApprovalContext,
        NullLoggerFactory LoggerFactory);

    private Deps BuildDeps(IChatClient? chatClient = null)
    {
        chatClient ??= Substitute.For<IChatClient>();

        var prompts = new FileSystemPromptRepository(NullLogger<FileSystemPromptRepository>.Instance);
        var providers = Array.Empty<IContextProvider>();
        var contextPipeline = new ContextPipeline(providers, NullLogger<ContextPipeline>.Instance);
        var traceStore = new InMemoryReasoningTraceStore();

        var approvalOpts = new ApprovalOptions { Enabled = false };
        var queue = new InMemoryApprovalQueue(
            Options.Create(approvalOpts),
            NullLogger<InMemoryApprovalQueue>.Instance);
        var sink = new InMemoryEscalationSink(NullLogger<InMemoryEscalationSink>.Instance);
        var tools = TestFactory.CreateToolsService(_fixture.ProductRepo, _fixture.OrderRepo, _fixture.ComplaintRepo);
        var escalationPolicy = new EscalationPolicyService(sink, Options.Create(approvalOpts));
        var approvalGate = new ApprovalGateService(
            queue, Options.Create(approvalOpts), sink, new ApprovalContextAccessor(), tools, escalationPolicy);

        return new Deps(
            chatClient,
            prompts,
            contextPipeline,
            traceStore,
            approvalGate,
            tools,
            new WorkflowGuardOptions { MaxIterations = 10, TimeoutSeconds = 30, MaxDuplicateToolCalls = 2 },
            new CustomerSupportBot.Application.Services.UiHint.UiHintEmitter(new ApprovalContextAccessor()),
            new ApprovalContextAccessor(),
            NullLoggerFactory.Instance);
    }

    private CustomerSupportTeam BuildTeam(IChatClient? chatClient = null)
    {
        var d = BuildDeps(chatClient);
        return new CustomerSupportTeam(
            d.ChatClient,
            d.ContextPipeline,
            Options.Create(d.Guards),
            Options.Create(new ParallelExecutionOptions()),
            d.TraceStore,
            d.Prompts,
            d.ApprovalGate,
            d.Tools,
            d.UiHint,
            d.ApprovalContext,
            d.LoggerFactory);
    }

    private AgentTeamFactory BuildAgentTeamFactory(IChatClient? chatClient = null)
    {
        var d = BuildDeps(chatClient);
        return new AgentTeamFactory(d.ChatClient, d.Prompts, d.ApprovalGate, d.Tools, d.Guards, d.LoggerFactory);
    }

    private WorkflowRunner BuildWorkflowRunner(IChatClient? chatClient = null)
    {
        var d = BuildDeps(chatClient);
        var factory = new AgentTeamFactory(d.ChatClient, d.Prompts, d.ApprovalGate, d.Tools, d.Guards, d.LoggerFactory);
        var finalizer = new TurnFinalizer(d.TraceStore, d.ApprovalGate, d.LoggerFactory, semanticMemory: null, profileService: null);
        return new WorkflowRunner(
            factory, finalizer, d.ContextPipeline, d.ChatClient, d.Guards, d.TraceStore, d.Prompts,
            d.ApprovalGate, d.UiHint, d.ApprovalContext, d.LoggerFactory);
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
        var factory = BuildAgentTeamFactory();
        var workflow = factory.CreateWorkflow();
        workflow.Should().NotBeNull();
    }

    [Fact]
    public void CreateWorkflow_CalledTwice_ReturnsDistinctInstances()
    {
        var factory = BuildAgentTeamFactory();
        var w1 = factory.CreateWorkflow();
        var w2 = factory.CreateWorkflow();
        w1.Should().NotBeNull();
        w2.Should().NotBeNull();
        ReferenceEquals(w1, w2).Should().BeFalse();
    }

    [Fact]
    public void BuildReasoningSummaryHint_WithReasoning_ReturnsNonEmpty()
    {
        var runner = BuildWorkflowRunner();
        var method = typeof(WorkflowRunner).GetMethod(
            "BuildReasoningSummaryHint", BindingFlags.Instance | BindingFlags.NonPublic)!;

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
        var hint = method.Invoke(runner, new object[] { reasoning }) as string;
        hint.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task BuildWorkflowMessagesAsync_WithoutSession_ReturnsUserMessage()
    {
        var runner = BuildWorkflowRunner();
        var method = typeof(WorkflowRunner).GetMethod(
            "BuildWorkflowMessagesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var task = (Task)method.Invoke(runner, new object?[] { "merhaba", null, null, null })!;
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
        var runner = BuildWorkflowRunner();
        var method = typeof(WorkflowRunner).GetMethod(
            "BuildWorkflowMessagesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var history = new List<ConversationMessage>
        {
            new(ConversationRoles.User, "önceki mesaj"),
            new(ConversationRoles.Assistant, "önceki yanıt"),
        };
        var task = (Task)method.Invoke(runner, ["şimdiki", history, null, null])!;
        await task.ConfigureAwait(true);
        var messages = (task.GetType().GetProperty("Result")!.GetValue(task) as List<ChatMessage>)!;

        messages.Should().Contain(m => m.Text == "önceki mesaj");
        messages.Should().Contain(m => m.Text == "şimdiki");
    }

    [Fact]
    public async Task BuildWorkflowMessagesAsync_WithExtractableId_AddsEntityHint()
    {
        var runner = BuildWorkflowRunner();
        var method = typeof(WorkflowRunner).GetMethod(
            "BuildWorkflowMessagesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var task = (Task)method.Invoke(runner, new object?[] { "sipariş 1030 nerede?", null, null, null })!;
        await task.ConfigureAwait(true);
        var messages = (task.GetType().GetProperty("Result")!.GetValue(task) as List<ChatMessage>)!;

        // Entity hint genelde System rolünde eklenir
        messages.Should().Contain(m => m.Role == ChatRole.System);
    }

    // ResponseStreamFilter — ResponseAgent gerçek token akışından "TERMINATE: reason=..."
    // işaretini (ve ardından gelen self-critique JSON'unu) kullanıcıya sızdırmamalı.
    // WorkflowRunner içindeki private nested class olduğu için reflection ile örneklenip çağrılıyor.

    private static object NewFilter()
    {
        var type = typeof(WorkflowRunner).GetNestedType("ResponseStreamFilter", BindingFlags.NonPublic)!;
        return Activator.CreateInstance(type, nonPublic: true)!;
    }

    private static string Feed(object filter, string chunk)
        => (string)filter.GetType()
            .GetMethod("Feed", BindingFlags.Instance | BindingFlags.Public)!
            .Invoke(filter, [chunk])!;

    [Fact]
    public void ResponseStreamFilter_PlainTextNoMarker_PassesThroughEventually()
    {
        var filter = NewFilter();
        var emitted = Feed(filter, "Merhaba, ") + Feed(filter, "siparişiniz kargoya verildi.");
        emitted.Should().Contain("Merhaba");
    }

    [Fact]
    public void ResponseStreamFilter_MarkerInSingleChunk_CutsOffAtMarker()
    {
        var filter = NewFilter();
        var emitted = Feed(filter, "Cevabınız burada. TERMINATE: reason=completed");
        emitted.Should().Be("Cevabınız burada. ");
        emitted.Should().NotContain("TERMINATE");
    }

    [Fact]
    public void ResponseStreamFilter_MarkerSplitAcrossChunks_NeverLeaksPartialMarker()
    {
        var filter = NewFilter();
        var emitted = Feed(filter, "Tamamdır. TERM");
        emitted += Feed(filter, "INATE: reason=completed");

        emitted.Should().Be("Tamamdır. ");
        emitted.Should().NotContain("TERM");
    }

    [Fact]
    public void ResponseStreamFilter_AfterCutoff_SuppressesFurtherChunks()
    {
        var filter = NewFilter();
        Feed(filter, "Cevap. TERMINATE: reason=completed");
        var afterCutoff = Feed(filter, "\n```json\n{\"selfCritique\":\"...\"}\n```");

        afterCutoff.Should().BeEmpty();
    }
}

// Tests/Agents/CustomerSupportTeamTests.cs
// CustomerSupportTeam ctor + kompozisyon kökü olarak doğru koşuculara sahip olduğunu test eder.
// AgentTeamFactory/WorkflowRunner/TurnFinalizer/DecomposedRunner ayrı sınıflara bölündüğü için
// (bkz. dosyaların kendisi) CreateWorkflow/BuildReasoningSummaryHint/BuildWorkflowMessagesAsync
// testleri artık ilgili sınıfı hedefliyor.
//
// Buradaki testlerin HEPSİ gerçek bir WorkflowRunner/AgentTeamFactory örneği kurmayı gerektirir,
// bu da PostgresCatalogFixture (Testcontainers → Docker) demektir. WorkflowRunner'ın örnek
// gerektirmeyen saf mantığı (ResponseStreamFilter, Ensure* garantileri) bilinçli olarak
// WorkflowRunnerPureLogicTests'e taşındı — orası Docker'sız da koşar.
//
// Reflection KULLANILMAZ: test edilen üyeler internal yapılıp InternalsVisibleTo ile doğrudan
// çağrılır. Eskiden GetMethod(..., NonPublic)+Invoke kullanılıyordu; bu, üye yeniden
// adlandırıldığında derleme zamanında değil çalışma zamanında patlıyordu (EnsureOrderCancelCompletion
// → EnsureSideEffectToolCompletion yeniden adlandırmasında bu bir kez gerçekten yaşandı).
// internal zaten assembly dışına çıkmadığı için genel API yüzeyi genişlemiyor.

using CustomerSupportBot.Adapters.Agents;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Services.Providers;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Adapters.Persistence.FileSystem;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Services.Approval;
using CustomerSupportBot.Application.Services.Escalation;

namespace CustomerSupportBot.Adapters.Agents.Tests;

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
        var contextPipeline = new ContextPipeline(providers, Options.Create(new ContextPipelineOptions()), NullLogger<ContextPipeline>.Instance);
        var traceStore = new InMemoryReasoningTraceStore();

        var approvalOpts = new ApprovalOptions { Enabled = false };
        var queue = new InMemoryApprovalQueue(
            Options.Create(approvalOpts),
            new NoopApprovalExecutionRouter(), NullLogger<InMemoryApprovalQueue>.Instance);
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
            d.LoggerFactory,
            new CustomerIdentityHintBuilder(Substitute.For<ICustomerRepository>()));
    }

    private AgentTeamFactory BuildAgentTeamFactory(IChatClient? chatClient = null)
    {
        var d = BuildDeps(chatClient);
        return new AgentTeamFactory(d.ChatClient, d.Prompts, d.ApprovalGate, d.Tools, d.Guards, d.LoggerFactory, d.ApprovalContext);
    }

    private WorkflowMessageBuilder BuildMessageBuilder(IChatClient? chatClient = null, IContextPipeline? pipeline = null)
    {
        var d = BuildDeps(chatClient);
        return new WorkflowMessageBuilder(
            pipeline ?? d.ContextPipeline, d.Prompts, d.ChatClient, d.LoggerFactory,
            new CustomerIdentityHintBuilder(Substitute.For<ICustomerRepository>()));
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
        var builder = BuildMessageBuilder();

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
        var hint = builder.BuildReasoningSummaryHint(reasoning);
        hint.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task BuildWorkflowMessagesAsync_WithoutSession_ReturnsUserMessage()
    {
        var builder = BuildMessageBuilder();

        var messages = (await builder.BuildWorkflowMessagesAsync("merhaba", null, null, null)).Messages;
        messages.Should().NotBeNull();
        messages.Should().NotBeEmpty();
        messages.Last().Role.Should().Be(ChatRole.User);
        messages.Last().Text.Should().Be("merhaba");
    }

    [Fact]
    public async Task BuildWorkflowMessagesAsync_WithHistory_IncludesPreviousMessages()
    {
        var builder = BuildMessageBuilder();

        var history = new List<ConversationMessage>
        {
            new(ConversationRoles.User, "önceki mesaj"),
            new(ConversationRoles.Assistant, "önceki yanıt"),
        };
        var messages = (await builder.BuildWorkflowMessagesAsync("şimdiki", history, null, null)).Messages;

        messages.Should().Contain(m => m.Text == "önceki mesaj");
        messages.Should().Contain(m => m.Text == "şimdiki");
    }

    /// <summary>
    /// Bağlam sağlayıcılarına hangi sorgunun geçtiğini yakalar.
    /// </summary>
    private sealed class QueryCapturingProvider : IContextProvider
    {
        public string? SeenQuery { get; private set; }
        public string Name => "QueryCapture";
        public int Order => 1;
        public Task<string?> GetContextAsync(AgentSession session, string currentQuery, CancellationToken ct = default)
        {
            SeenQuery = currentQuery;
            return Task.FromResult<string?>(null);
        }
    }

    [Fact]
    public async Task BuildWorkflowMessagesAsync_PassesCurrentQueryToContextProviders()
    {
        // KABLOLAMA testi. Sağlayıcı seviyesindeki testler provider'ı doğrudan çağırdığı için
        // WorkflowRunner sorguyu geçirmeyi bıraksa bile yeşil kalıyor — mutasyon denemesinde
        // bu boşluk fiilen görüldü. Burası zincirin WorkflowRunner → ContextPipeline →
        // IContextProvider ucunu kilitler.
        //
        // Sorgu geçmişten okunamaz: geçmiş workflow bittikten SONRA yazılıyor. Bu yüzden
        // parametrenin gerçekten taşınması semantik hafıza retrieval'ının doğruluğu için şart.
        var capture = new QueryCapturingProvider();
        var pipeline = new ContextPipeline([capture], Options.Create(new ContextPipelineOptions()), NullLogger<ContextPipeline>.Instance);
        var builder = BuildMessageBuilder(pipeline: pipeline);

        var session = new AgentSession { SessionId = "s1", State = new SessionState() };
        await builder.BuildWorkflowMessagesAsync("iade süresi ne kadar?", null, session, null);

        capture.SeenQuery.Should().Be("iade süresi ne kadar?",
            "kullanıcının bu turdaki mesajı bağlam sağlayıcılarına geçirilmeli");
    }

    [Fact]
    public async Task BuildWorkflowMessagesAsync_WithExtractableId_AddsEntityHint()
    {
        var builder = BuildMessageBuilder();

        var messages = (await builder.BuildWorkflowMessagesAsync("sipariş 1030 nerede?", null, null, null)).Messages;

        // Entity hint genelde System rolünde eklenir
        messages.Should().Contain(m => m.Role == ChatRole.System);
    }

}

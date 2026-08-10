// Tests/Evaluation/EvaluationRunnerTests.cs

using CustomerSupportBot.Adapters.AI.Chat;
using CustomerSupportBot.Adapters.Agents;
using CustomerSupportBot.Api.Infrastructure;
using CustomerSupportBot.Adapters.Agents.Evaluation;
using CustomerSupportBot.Adapters.Persistence.FileSystem;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Api.Tests.Helpers;
using CustomerSupportBot.Api.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using EntityVerifier = CustomerSupportBot.Application.Services.Reasoning.EntityVerifier;

namespace CustomerSupportBot.Api.Tests.Evaluation;

[Collection("PostgresCatalog")]
public class EvaluationRunnerTests
{
    private readonly PostgresCatalogFixture _fixture;

    public EvaluationRunnerTests(PostgresCatalogFixture fixture)
    {
        _fixture = fixture;
    }

    private EvaluationRunner BuildRunner()
    {
        var chatClient = Substitute.For<IChatClient>();
        var prompts = new FileSystemPromptRepository(NullLogger<FileSystemPromptRepository>.Instance);
        var configuration = new ConfigurationBuilder().Build();
        var contextPipeline = new ContextPipeline(
            Array.Empty<IContextProvider>(), NullLogger<ContextPipeline>.Instance);
        var traceStore = new InMemoryReasoningTraceStore();
        var approvalOpts = new ApprovalOptions { Enabled = false };
        var queue = new InMemoryApprovalQueue(
            Options.Create(approvalOpts), new NoopApprovalExecutionRouter(), NullLogger<InMemoryApprovalQueue>.Instance);
        var sink = new InMemoryEscalationSink(NullLogger<InMemoryEscalationSink>.Instance);
        var tools = TestFactory.CreateToolsService(_fixture.ProductRepo, _fixture.OrderRepo, _fixture.ComplaintRepo);
        var escalationPolicy = new EscalationPolicyService(
            sink, Options.Create(approvalOpts));
        var approvalGate = new ApprovalGateService(
            queue, Options.Create(approvalOpts), sink, new ApprovalContextAccessor(), tools, escalationPolicy);

        var team = new CustomerSupportTeam(
            chatClient, contextPipeline,
            Options.Create(new WorkflowGuardOptions()),
            Options.Create(new ParallelExecutionOptions()),
            traceStore,
            prompts, approvalGate, tools,
            new CustomerSupportBot.Application.Services.UiHint.UiHintEmitter(new ApprovalContextAccessor()),
            new ApprovalContextAccessor(),
            NullLoggerFactory.Instance,
            new CustomerIdentityHintBuilder(Substitute.For<ICustomerRepository>()));

        var reasoningClient = new ReasoningChatClient(chatClient, "gpt-test", "low");
        var entityVerifier = new EntityVerifier(
            _fixture.OrderRepo,
            _fixture.ComplaintRepo,
            NullLogger<EntityVerifier>.Instance);
        var sanityChecker = new ReasoningSanityChecker(NullLogger<ReasoningSanityChecker>.Instance);
        var reasoningService = new ReasoningService(
            reasoningClient,
            NullLogger<ReasoningService>.Instance,
            prompts,
            entityVerifier,
            sanityChecker);

        var distributedLock = new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 }));
        var sessionManager = new InMemorySessionManager(distributedLock);

        return new EvaluationRunner(
            team, reasoningService, sessionManager, traceStore,
            chatClient, Options.Create(new EvaluationQualityOptions()));
    }

    // ─── LoadScenarios ───

    [Fact]
    public void LoadScenarios_ValidYaml_DeserializesScenarios()
    {
        var yaml = """
            version: 1
            scenarios:
              - id: order-1
                category: order
                query: "siparişim nerede"
                expected_intent: order_inquiry
                expected_agents: [PlanningAgent, OrderAgent]
                expected_tools: [order_status_tool]
                success_criteria:
                  - type: contains_any
                    values: ["durum"]
                  - type: turn_count
                    op: "<="
                    value: 5
              - id: complaint-1
                category: complaint
                query: "ürün bozuk geldi"
                expected_tools: []
                success_criteria: []
            """;
        var path = Path.Combine(Path.GetTempPath(), $"scenarios-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, yaml);
        try
        {
            var file = ScenarioLoader.LoadScenarios(path);
            file.Version.Should().Be(1);
            file.Scenarios.Should().HaveCount(2);

            var s1 = file.Scenarios[0];
            s1.Id.Should().Be("order-1");
            s1.Category.Should().Be("order");
            s1.Query.Should().Be("siparişim nerede");
            s1.ExpectedIntent.Should().Be("order_inquiry");
            s1.ExpectedAgents.Should().HaveCount(2);
            s1.ExpectedTools.Should().ContainSingle().Which.Should().Be("order_status_tool");
            s1.SuccessCriteria.Should().HaveCount(2);

            var s2 = file.Scenarios[1];
            s2.Id.Should().Be("complaint-1");
            s2.ExpectedTools.Should().BeEmpty();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadScenarios_UnknownProperty_Ignored()
    {
        var yaml = """
            version: 2
            scenarios:
              - id: a
                category: x
                query: q
                some_unknown_field: value
            """;
        var path = Path.Combine(Path.GetTempPath(), $"scen-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, yaml);
        try
        {
            var f = ScenarioLoader.LoadScenarios(path);
            f.Version.Should().Be(2);
            f.Scenarios.Should().ContainSingle();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadScenarios_EmptyScenarios_ReturnsEmptyList()
    {
        var yaml = "version: 1\nscenarios: []";
        var path = Path.Combine(Path.GetTempPath(), $"scen-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, yaml);
        try
        {
            var f = ScenarioLoader.LoadScenarios(path);
            f.Scenarios.Should().BeEmpty();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadScenarios_NonExistentFile_Throws()
    {
        var act = () => ScenarioLoader.LoadScenarios(Path.Combine(Path.GetTempPath(), "does-not-exist.yaml"));
        act.Should().Throw<FileNotFoundException>();
    }

    // ─── RunAsync ───

    [Fact]
    public async Task RunAsync_EmptyScenarios_CompletesWithZeroResults()
    {
        var runner = BuildRunner();
        var result = await runner.RunAsync(new List<EvaluationScenario>(), CancellationToken.None);

        result.TotalScenarios.Should().Be(0);
        result.PassedScenarios.Should().Be(0);
        result.FailedScenarios.Should().Be(0);
        result.Results.Should().BeEmpty();
        result.CompletedAt.Should().BeOnOrAfter(result.StartedAt);
    }

    [Fact]
    public async Task RunAsync_PreCancelledToken_BreaksImmediately()
    {
        var runner = BuildRunner();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var scenarios = new List<EvaluationScenario>
        {
            new() { Id = "s1", Category = "x", Query = "q1" },
            new() { Id = "s2", Category = "x", Query = "q2" },
        };
        var result = await runner.RunAsync(scenarios, cts.Token);

        result.TotalScenarios.Should().Be(2);
        result.Results.Should().BeEmpty(); // hiç çalıştırmadan break
    }

    // ─── RunScenarioAsync ───
    // Not: Tam akış reasoning + workflow gerektirdiğinden CancellationToken alsa bile
    // Mock IChatClient ile workflow başlangıcı asılı kalabiliyor; bu yüzden yalnızca
    // RunAsync seviyesinde pre-cancellation kapsamı yeterli.
}

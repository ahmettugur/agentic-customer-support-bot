// Tests/Evaluation/EvaluationRunnerTests.cs
using CustomerSupportBot.Agents;
using CustomerSupportBot.Evaluation;
using CustomerSupportBot.Models;
using CustomerSupportBot.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Tests.Evaluation;

public class EvaluationRunnerTests
{
    private static EvaluationRunner BuildRunner()
    {
        var chatClient = Substitute.For<IChatClient>();
        var prompts = new PromptService(NullLogger<PromptService>.Instance);
        var configuration = new ConfigurationBuilder().Build();
        var contextPipeline = new ContextPipeline(
            Array.Empty<IContextProvider>(), NullLogger<ContextPipeline>.Instance);
        var traceStore = new InMemoryReasoningTraceStore();
        var revisionService = new RevisionService(chatClient, prompts);
        var approvalOpts = new ApprovalOptions { Enabled = false };
        var queue = new InMemoryApprovalQueue(
            Options.Create(approvalOpts), NullLogger<InMemoryApprovalQueue>.Instance);
        var sink = new InMemoryEscalationSink(NullLogger<InMemoryEscalationSink>.Instance);
        var approvalGate = new ApprovalGateService(
            queue, Options.Create(approvalOpts), sink, new ApprovalContextAccessor());

        var team = new CustomerSupportTeam(
            chatClient, contextPipeline, configuration, traceStore,
            revisionService, prompts, approvalGate, NullLoggerFactory.Instance);

        var reasoningClient = new ReasoningChatClient(chatClient, "gpt-test", "low");
        var entityVerifier = new EntityVerifier(NullLogger<EntityVerifier>.Instance);
        var sanityChecker = new ReasoningSanityChecker(NullLogger<ReasoningSanityChecker>.Instance);
        var reasoningService = new ReasoningService(
            reasoningClient,
            NullLogger<ReasoningService>.Instance,
            prompts,
            entityVerifier,
            sanityChecker);

        var sessionManager = new InMemorySessionManager();

        return new EvaluationRunner(team, reasoningService, sessionManager, traceStore);
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
                expected_agents: [PlanningAgent, OrderInquiryAgent]
                expected_tools: [order_status_tool]
                success_criteria:
                  - "response contains 'durum'"
                  - "turn_count <= 5"
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
            var file = EvaluationRunner.LoadScenarios(path);
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
            var f = EvaluationRunner.LoadScenarios(path);
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
            var f = EvaluationRunner.LoadScenarios(path);
            f.Scenarios.Should().BeEmpty();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadScenarios_NonExistentFile_Throws()
    {
        var act = () => EvaluationRunner.LoadScenarios(Path.Combine(Path.GetTempPath(), "does-not-exist.yaml"));
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

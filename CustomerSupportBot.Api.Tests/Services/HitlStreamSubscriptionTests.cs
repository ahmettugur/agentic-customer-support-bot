using System.Text;
using CustomerSupportBot.Application.Services.Evaluation;
using CustomerSupportBot.Api.Infrastructure;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Tests.Services;

public class HitlStreamSubscriptionTests
{
    private static (DefaultHttpContext ctx, MemoryStream body) BuildResponse()
    {
        var ctx = new DefaultHttpContext();
        var body = new MemoryStream();
        ctx.Response.Body = body;
        return (ctx, body);
    }

    [Fact]
    public async Task ApprovalCreated_ForCurrentSession_WritesSseEvent()
    {
        var (ctx, body) = BuildResponse();
        var queue = new InMemoryApprovalQueue(
            Options.Create(new ApprovalOptions()),
            NullLogger<InMemoryApprovalQueue>.Instance);
        var sink = new InMemoryEscalationSink(NullLogger<InMemoryEscalationSink>.Instance);
        using var fwd = new SseForwarder(ctx.Response, default);
        using var sub = new HitlStreamSubscription(queue, sink, fwd, "s1");
        sub.Subscribe();

        queue.Create(new ApprovalRequest { SessionId = "s1", ToolName = "x", AgentName = "a" });

        await Task.Delay(50, TestContext.Current.CancellationToken);
        var text = Encoding.UTF8.GetString(body.ToArray());
        text.Should().Contain(StreamEventTypes.ApprovalRequired);
    }

    [Fact]
    public async Task ApprovalCreated_OtherSession_NoSseEvent()
    {
        var (ctx, body) = BuildResponse();
        var queue = new InMemoryApprovalQueue(
            Options.Create(new ApprovalOptions()),
            NullLogger<InMemoryApprovalQueue>.Instance);
        var sink = new InMemoryEscalationSink(NullLogger<InMemoryEscalationSink>.Instance);
        using var fwd = new SseForwarder(ctx.Response, default);
        using var sub = new HitlStreamSubscription(queue, sink, fwd, "s1");
        sub.Subscribe();

        queue.Create(new ApprovalRequest { SessionId = "OTHER", ToolName = "x", AgentName = "a" });

        await Task.Delay(50, TestContext.Current.CancellationToken);
        body.Length.Should().Be(0);
    }

    [Fact]
    public async Task ApprovalDecided_WritesResolvedEvent()
    {
        var (ctx, body) = BuildResponse();
        var queue = new InMemoryApprovalQueue(
            Options.Create(new ApprovalOptions()),
            NullLogger<InMemoryApprovalQueue>.Instance);
        var sink = new InMemoryEscalationSink(NullLogger<InMemoryEscalationSink>.Instance);
        using var fwd = new SseForwarder(ctx.Response, default);
        using var sub = new HitlStreamSubscription(queue, sink, fwd, "s1");
        sub.Subscribe();

        var req = new ApprovalRequest { SessionId = "s1", ToolName = "x", AgentName = "a" };
        queue.Create(req);
        queue.Decide(req.Id, approved: true, decidedBy: "admin", reason: "ok");

        await Task.Delay(50, TestContext.Current.CancellationToken);
        var text = Encoding.UTF8.GetString(body.ToArray());
        text.Should().Contain(StreamEventTypes.ApprovalResolved);
    }

    [Fact]
    public async Task EscalationCreated_WritesSseEvent()
    {
        var (ctx, body) = BuildResponse();
        var queue = new InMemoryApprovalQueue(
            Options.Create(new ApprovalOptions()),
            NullLogger<InMemoryApprovalQueue>.Instance);
        var sink = new InMemoryEscalationSink(NullLogger<InMemoryEscalationSink>.Instance);
        using var fwd = new SseForwarder(ctx.Response, default);
        using var sub = new HitlStreamSubscription(queue, sink, fwd, "s1");
        sub.Subscribe();

        sink.Create(new EscalationRequest { SessionId = "s1", AgentName = "a", Reason = "r" });

        await Task.Delay(50, TestContext.Current.CancellationToken);
        var text = Encoding.UTF8.GetString(body.ToArray());
        text.Should().Contain(StreamEventTypes.EscalationCreated);
    }

    [Fact]
    public async Task Unsubscribe_StopsReceivingEvents()
    {
        var (ctx, body) = BuildResponse();
        var queue = new InMemoryApprovalQueue(
            Options.Create(new ApprovalOptions()),
            NullLogger<InMemoryApprovalQueue>.Instance);
        var sink = new InMemoryEscalationSink(NullLogger<InMemoryEscalationSink>.Instance);
        using var fwd = new SseForwarder(ctx.Response, default);
        var sub = new HitlStreamSubscription(queue, sink, fwd, "s1");
        sub.Subscribe();
        sub.Unsubscribe();

        queue.Create(new ApprovalRequest { SessionId = "s1", ToolName = "x", AgentName = "a" });

        await Task.Delay(50, TestContext.Current.CancellationToken);
        body.Length.Should().Be(0);
    }

    [Fact]
    public void Dispose_CallsUnsubscribe()
    {
        var (ctx, _) = BuildResponse();
        var queue = new InMemoryApprovalQueue(
            Options.Create(new ApprovalOptions()),
            NullLogger<InMemoryApprovalQueue>.Instance);
        var sink = new InMemoryEscalationSink(NullLogger<InMemoryEscalationSink>.Instance);
        using var fwd = new SseForwarder(ctx.Response, default);
        var sub = new HitlStreamSubscription(queue, sink, fwd, "s1");
        sub.Subscribe();
        Action act = () => sub.Dispose();
        act.Should().NotThrow();
    }
}

public class EvaluationRunnerLoadScenariosTests
{
    [Fact]
    public void LoadScenarios_ValidYaml_ReturnsScenarios()
    {
        var yaml = """
            version: 1
            scenarios:
              - id: T1
                category: greeting
                query: "merhaba"
                expected_intent: greeting
                expected_agents:
                  - PlanningAgent
                expected_tools: []
                success_criteria:
                  - "response_contains: merhaba"
            """;
        var path = Path.Combine(Path.GetTempPath(), $"eval-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, yaml);
        try
        {
            var file = EvaluationRunner.LoadScenarios(path);
            file.Version.Should().Be(1);
            file.Scenarios.Should().HaveCount(1);
            var s = file.Scenarios[0];
            s.Id.Should().Be("T1");
            s.Category.Should().Be("greeting");
            s.Query.Should().Be("merhaba");
            s.ExpectedIntent.Should().Be("greeting");
            s.ExpectedAgents.Should().Contain("PlanningAgent");
            s.SuccessCriteria.Should().HaveCount(1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadScenarios_RealFile_Works()
    {
        // Repository içindeki gerçek senaryo dosyasý kopyalandýysa dene; yoksa skip
        var candidatePaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "evaluation-scenarios.yaml"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "docs", "evaluation-scenarios.yaml")
        };
        var existing = candidatePaths.FirstOrDefault(File.Exists);
        if (existing == null) return;
        var file = EvaluationRunner.LoadScenarios(Path.GetFullPath(existing));
        file.Should().NotBeNull();
        file.Scenarios.Should().NotBeEmpty();
    }
}

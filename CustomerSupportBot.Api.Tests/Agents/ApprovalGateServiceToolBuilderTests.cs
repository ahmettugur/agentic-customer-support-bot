using System.Text.Json;
using CustomerSupportBot.Adapters.Agents;
using CustomerSupportBot.Api.Tests.Infrastructure;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Api.Tests.Helpers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Tests.Agents;

// NOT: HITL onay bekletme mantığı artık ApprovalRequiredAIFunction + FunctionInvokingChatClient
// tarafından yönetiliyor (framework seviyesinde), tool lambda'sının içinde DEĞİL. Bu yüzden
// AIFunction.InvokeAsync'i doğrudan çağırmak onay kapısını hiç tetiklemez — ApprovalRequiredAIFunction
// saf bir işaretleyicidir, InvokeCoreAsync'i doğrudan iç fonksiyona delege eder (bkz.
// BuildOrderPlacementTool_WrappedFunction_InvokeAsyncBypassesGate testi). Bu yüzden testler artık
// iki ayrı seviyeyi doğruluyor: (1) Build*Tool()'un doğru koşullarda sarmalayıp sarmalamadığı,
// (2) RequestApprovalAsync'in (artık WorkflowRunner tarafından çağrılan public metot) IApprovalQueue
// ile doğru etkileşimi. Gerçek uçtan uca ("LLM tool çağırmaya karar verir → framework duraklatır →
// admin onaylar → workflow devam eder") akışı, çalışan bir workflow + gerçek LLM gerektirir; bu
// birim testlerinin kapsamı dışındadır.
[Collection("PostgresCatalog")]
public class ApprovalGateServiceToolBuilderTests
{
    private readonly PostgresCatalogFixture _fixture;

    public ApprovalGateServiceToolBuilderTests(PostgresCatalogFixture fixture)
    {
        _fixture = fixture;
    }

    private ApprovalGateService Build(ApprovalOptions opts, IApprovalQueue? queue = null)
    {
        queue ??= new InMemoryApprovalQueue(
            Options.Create(opts),
            NullLogger<InMemoryApprovalQueue>.Instance);
        var sink = new InMemoryEscalationSink(NullLogger<InMemoryEscalationSink>.Instance);
        var escalationPolicy = new EscalationPolicyService(
            sink, Options.Create(opts));
        return new ApprovalGateService(
            queue,
            Options.Create(opts),
            sink,
            new ApprovalContextAccessor(),
            TestFactory.CreateToolsService(_fixture.ProductRepo, _fixture.OrderRepo, _fixture.ComplaintRepo),
            escalationPolicy);
    }

    /// <summary>
    /// AIFunctionFactory wraps the result and may serialize it to JsonElement.
    /// Parse out the success/message fields uniformly.
    /// </summary>
    private static (bool success, string message) ParseResult(object? raw)
    {
        if (raw is ToolResult tr) return (tr.Success, tr.Message ?? "");
        if (raw is JsonElement je)
        {
            var success = je.TryGetProperty("success", out var s) && s.GetBoolean();
            var message = je.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            return (success, message);
        }
        return (false, raw?.ToString() ?? "");
    }

    // ── Build*Tool() sarmalama kararı ───────────────────────────────────────────

    [Fact]
    public void BuildOrderPlacementTool_ApprovalRequired_WrapsWithApprovalRequiredAIFunction()
    {
        var opts = new ApprovalOptions
        {
            Enabled = true,
            ToolsRequiringApproval = new() { WellKnown.ToolNames.OrderPlacement }
        };
        var svc = Build(opts);

        svc.BuildOrderPlacementTool().Should().BeOfType<ApprovalRequiredAIFunction>();
    }

    [Fact]
    public void BuildOrderPlacementTool_ApprovalDisabled_NotWrapped()
    {
        var opts = new ApprovalOptions { Enabled = false };
        var svc = Build(opts);

        svc.BuildOrderPlacementTool().Should().NotBeOfType<ApprovalRequiredAIFunction>();
    }

    [Fact]
    public void BuildOrderPlacementTool_ToolNotInList_NotWrapped()
    {
        var opts = new ApprovalOptions { Enabled = true, ToolsRequiringApproval = new() };
        var svc = Build(opts);

        svc.BuildOrderPlacementTool().Should().NotBeOfType<ApprovalRequiredAIFunction>();
    }

    [Fact]
    public void BuildComplaintRegistrationTool_ApprovalRequired_WrapsWithApprovalRequiredAIFunction()
    {
        var opts = new ApprovalOptions
        {
            Enabled = true,
            ToolsRequiringApproval = new() { WellKnown.ToolNames.ComplaintRegistration }
        };
        var svc = Build(opts);

        svc.BuildComplaintRegistrationTool().Should().BeOfType<ApprovalRequiredAIFunction>();
    }

    [Fact]
    public void BuildComplaintRegistrationTool_ApprovalDisabled_NotWrapped()
    {
        var opts = new ApprovalOptions { Enabled = false };
        var svc = Build(opts);

        svc.BuildComplaintRegistrationTool().Should().NotBeOfType<ApprovalRequiredAIFunction>();
    }

    [Fact]
    public void BuildOrderCancelTool_ApprovalRequired_WrapsWithApprovalRequiredAIFunction()
    {
        var opts = new ApprovalOptions
        {
            Enabled = true,
            ToolsRequiringApproval = new() { WellKnown.ToolNames.OrderCancel }
        };
        var svc = Build(opts);

        svc.BuildOrderCancelTool().Should().BeOfType<ApprovalRequiredAIFunction>();
    }

    [Fact]
    public void BuildReturnRequestTool_ApprovalRequired_WrapsWithApprovalRequiredAIFunction()
    {
        var opts = new ApprovalOptions
        {
            Enabled = true,
            ToolsRequiringApproval = new() { WellKnown.ToolNames.ReturnRequest }
        };
        var svc = Build(opts);

        svc.BuildReturnRequestTool().Should().BeOfType<ApprovalRequiredAIFunction>();
    }

    // ── InvokeAsync doğrudan çağrıldığında (gate'siz path) tool gerçekten çalışıyor mu ──

    [Fact]
    public async Task BuildOrderPlacementTool_ApprovalDisabled_PassesThroughToTool()
    {
        var opts = new ApprovalOptions { Enabled = false };
        var svc = Build(opts);
        var fn = svc.BuildOrderPlacementTool();

        var product = _fixture.ProductRepo.GetAll().First().Name;

        var result = await fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["productName"] = product,
            ["quantity"] = 1,
            ["customerId"] = $"9007"
        }), TestContext.Current.CancellationToken);
        var (success, _) = ParseResult(result);
        success.Should().BeTrue();
    }

    [Fact]
    public async Task BuildOrderPlacementTool_ToolNotInList_BypassedAndCallsTool()
    {
        var opts = new ApprovalOptions
        {
            Enabled = true,
            ToolsRequiringApproval = new()
        };
        var svc = Build(opts);
        var fn = svc.BuildOrderPlacementTool();
        var product = _fixture.ProductRepo.GetAll().First().Name;

        var result = await fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["productName"] = product,
            ["quantity"] = 1,
            ["customerId"] = $"9008"
        }), TestContext.Current.CancellationToken);
        var (success, _) = ParseResult(result);
        success.Should().BeTrue();
    }

    [Fact]
    public async Task BuildOrderPlacementTool_WrappedFunction_InvokeAsyncBypassesGate_DelegatesDirectlyToInnerTool()
    {
        // ApprovalRequiredAIFunction saf bir işaretleyicidir (DelegatingAIFunction.InvokeCoreAsync
        // doğrudan iç fonksiyona delege eder) — gerçek engelleme FunctionInvokingChatClient'ta
        // olur, burada değil. Bu test o davranışı belgeliyor: InvokeAsync'i doğrudan çağırmak
        // (ör. bir test, ya da normal chat-completion pipeline'ı dışındaki bir kod yolu) onay
        // kapısını devre dışı bırakır ve gerçek tool'u çalıştırır.
        var opts = new ApprovalOptions
        {
            Enabled = true,
            ToolsRequiringApproval = new() { WellKnown.ToolNames.OrderPlacement }
        };
        var svc = Build(opts);
        var fn = svc.BuildOrderPlacementTool();
        fn.Should().BeOfType<ApprovalRequiredAIFunction>();

        var product = _fixture.ProductRepo.GetAll().First().Name;
        var result = await fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["productName"] = product,
            ["quantity"] = 1,
            ["customerId"] = "9011"
        }), TestContext.Current.CancellationToken);

        var (success, _) = ParseResult(result);
        success.Should().BeTrue();
    }

    [Fact]
    public async Task BuildComplaintRegistrationTool_ApprovalDisabled_BypassesGate()
    {
        var opts = new ApprovalOptions { Enabled = false };
        var svc = Build(opts);
        var fn = svc.BuildComplaintRegistrationTool();

        var result = await fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["orderId"] = "1030",
            ["complaintText"] = $"şikayet metni unique {Guid.NewGuid()} buraya yazıldı",
            ["customerId"] = "1027"
        }), TestContext.Current.CancellationToken);
        var (success, _) = ParseResult(result);
        success.Should().BeTrue();
    }

    // ── RequestApprovalAsync — WorkflowRunner'ın RequestInfoEvent köprüsünden çağırdığı metot ──

    [Fact]
    public async Task RequestApprovalAsync_Rejected_ReturnsApprovedFalseWithReason()
    {
        var opts = new ApprovalOptions { Enabled = true, TimeoutSeconds = 5 };
        var queue = new InMemoryApprovalQueue(
            Options.Create(opts),
            NullLogger<InMemoryApprovalQueue>.Instance);
        queue.RequestCreated += (_, req) =>
            _ = queue.DecideAsync(req.Id, approved: false, decidedBy: "test", reason: "test_reject");

        var svc = Build(opts, queue);

        var decision = await svc.RequestApprovalAsync(
            WellKnown.ToolNames.OrderPlacement,
            WellKnown.AgentNames.Order,
            new Dictionary<string, object?> { ["customerId"] = "9009" },
            justification: null,
            TestContext.Current.CancellationToken);

        decision.Approved.Should().BeFalse();
        decision.Reason.Should().Be("test_reject");
    }

    [Fact]
    public async Task RequestApprovalAsync_Approved_ReturnsApprovedTrue()
    {
        var opts = new ApprovalOptions { Enabled = true, TimeoutSeconds = 5 };
        var queue = new InMemoryApprovalQueue(
            Options.Create(opts),
            NullLogger<InMemoryApprovalQueue>.Instance);
        queue.RequestCreated += (_, req) =>
            _ = queue.DecideAsync(req.Id, approved: true, decidedBy: "test", reason: "ok");

        var svc = Build(opts, queue);

        var decision = await svc.RequestApprovalAsync(
            WellKnown.ToolNames.ComplaintRegistration,
            WellKnown.AgentNames.Complaint,
            new Dictionary<string, object?> { ["orderId"] = "1042" },
            justification: null,
            TestContext.Current.CancellationToken);

        decision.Approved.Should().BeTrue();
    }

    [Fact]
    public async Task RequestApprovalAsync_NullParameters_TreatedAsEmptyDictionary()
    {
        var opts = new ApprovalOptions { Enabled = true, TimeoutSeconds = 5 };
        var queue = new InMemoryApprovalQueue(
            Options.Create(opts),
            NullLogger<InMemoryApprovalQueue>.Instance);
        queue.RequestCreated += (_, req) =>
            _ = queue.DecideAsync(req.Id, approved: true, decidedBy: "test", reason: null);

        var svc = Build(opts, queue);

        var decision = await svc.RequestApprovalAsync(
            WellKnown.ToolNames.OrderPlacement,
            WellKnown.AgentNames.Order,
            parameters: null,
            justification: null,
            TestContext.Current.CancellationToken);

        decision.Approved.Should().BeTrue();
    }

    // ── ResolveAgentName ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(WellKnown.ToolNames.OrderPlacement)]
    [InlineData(WellKnown.ToolNames.OrderCancel)]
    [InlineData(WellKnown.ToolNames.ReturnRequest)]
    public void ResolveAgentName_OrderTools_ReturnsOrderAgent(string toolName)
    {
        ApprovalGateService.ResolveAgentName(toolName).Should().Be(WellKnown.AgentNames.Order);
    }

    [Fact]
    public void ResolveAgentName_ComplaintTool_ReturnsComplaintAgent()
    {
        ApprovalGateService.ResolveAgentName(WellKnown.ToolNames.ComplaintRegistration)
            .Should().Be(WellKnown.AgentNames.Complaint);
    }

    [Fact]
    public void ResolveAgentName_UnknownTool_ReturnsFallback()
    {
        ApprovalGateService.ResolveAgentName("some_unmapped_tool").Should().Be("UnknownAgent");
    }
}

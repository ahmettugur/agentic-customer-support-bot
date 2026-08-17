using System.Text.Json;
using CustomerSupportBot.Adapters.Agents;
using CustomerSupportBot.Tests.Shared;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Approval;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Services.Escalation;

namespace CustomerSupportBot.Adapters.Agents.Tests;

// NOT: Bloklamayan onay modeli (#48) — onay gerektiren 4 tool (sipariş/iptal/iade/şikayet)
// artık ApprovalRequiredAIFunction ile SARILMIYOR ve admin kararını beklemiyor.
// ApprovalGateService.ExecuteWithApprovalGateAsync, onay gerekiyorsa IApprovalQueue.CreateAsync
// ile kaydı oluşturup HEMEN "onaya gönderildi" (pending) ToolResult'ı döner — gerçek iş tool
// çağrısı anında ÇALIŞTIRILMAZ, admin karar verdiğinde IApprovalExecutionRouter üzerinden
// DecideAsync anında tetiklenir (bkz. ApprovalExecutionRouter/PostgresApprovalQueue testleri).
// customerId artık LLM'e sorulan bir parametre DEĞİL — ApprovalContextAccessor'daki (JWT'den
// gelen) CustomerId kullanılır (bkz. #54), bu yüzden customerId gerektiren tool'ları test
// ederken context SetScope ile kurulmalı.
[Collection("PostgresCatalog")]
public class ApprovalGateServiceToolBuilderTests
{
    private readonly PostgresCatalogFixture _fixture;

    public ApprovalGateServiceToolBuilderTests(PostgresCatalogFixture fixture)
    {
        _fixture = fixture;
    }

    private ApprovalGateService Build(
        ApprovalOptions opts, IApprovalQueue? queue = null, ApprovalContextAccessor? contextAccessor = null)
    {
        queue ??= new InMemoryApprovalQueue(
            Options.Create(opts),
            new NoopApprovalExecutionRouter(), NullLogger<InMemoryApprovalQueue>.Instance);
        var sink = new InMemoryEscalationSink(NullLogger<InMemoryEscalationSink>.Instance);
        var escalationPolicy = new EscalationPolicyService(
            sink, Options.Create(opts));
        return new ApprovalGateService(
            queue,
            Options.Create(opts),
            sink,
            contextAccessor ?? new ApprovalContextAccessor(),
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

    // ── Bloklamayan onay: tool artık admin kararını beklemiyor ─────────────────────

    [Fact]
    public async Task BuildOrderPlacementTool_ApprovalRequired_ReturnsPendingWithoutExecutingTool()
    {
        var opts = new ApprovalOptions
        {
            Enabled = true,
            ToolsRequiringApproval = new() { WellKnown.ToolNames.OrderPlacement }
        };
        // Auto-decide YOK — DecideAsync hiç çağrılmıyor, kayıt Pending kalmalı.
        var queue = new InMemoryApprovalQueue(
            Options.Create(opts), new NoopApprovalExecutionRouter(), NullLogger<InMemoryApprovalQueue>.Instance);
        var contextAccessor = new ApprovalContextAccessor();
        using var scope = contextAccessor.SetScope("s1", null, "sipariş ver", "9011");
        var svc = Build(opts, queue, contextAccessor);
        var fn = svc.BuildOrderPlacementTool();

        var product = _fixture.ProductRepo.GetAll().First().Name;
        var result = await fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["lines"] = new[] { new OrderLineRequest(product, 1) }
        }), TestContext.Current.CancellationToken);

        var (success, message) = ParseResult(result);
        success.Should().BeTrue("pending dönüş de bir ToolResult.Ok'tur — tool başarısız olmadı, sadece ertelendi");
        message.Should().Contain("onaya gönderildi");

        queue.GetPending().Should().ContainSingle(p => p.ToolName == WellKnown.ToolNames.OrderPlacement);
    }

    [Fact]
    public async Task BuildComplaintRegistrationTool_ApprovalRequired_ReturnsPendingWithoutExecutingTool()
    {
        var opts = new ApprovalOptions
        {
            Enabled = true,
            ToolsRequiringApproval = new() { WellKnown.ToolNames.ComplaintRegistration }
        };
        var queue = new InMemoryApprovalQueue(
            Options.Create(opts), new NoopApprovalExecutionRouter(), NullLogger<InMemoryApprovalQueue>.Instance);
        // Ön kontrol sahiplik doğruladığı için sipariş 1030'un gerçek sahibi (1027) olarak çağır.
        var contextAccessor = new ApprovalContextAccessor();
        using var scope = contextAccessor.SetScope("s1", null, "şikayet", "1027");
        var svc = Build(opts, queue, contextAccessor);
        var fn = svc.BuildComplaintRegistrationTool();

        var result = await fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["orderId"] = "1030",
            ["complaintText"] = "onaya düşmesi beklenen şikayet metni"
        }), TestContext.Current.CancellationToken);

        var (success, message) = ParseResult(result);
        success.Should().BeTrue();
        message.Should().Contain("onaya gönderildi");
        queue.GetPending().Should().ContainSingle(p => p.ToolName == WellKnown.ToolNames.ComplaintRegistration);
    }

    [Fact]
    public async Task BuildOrderCancelTool_ApprovalRequired_ReturnsPendingWithoutExecutingTool()
    {
        var opts = new ApprovalOptions
        {
            Enabled = true,
            ToolsRequiringApproval = new() { WellKnown.ToolNames.OrderCancel }
        };
        var queue = new InMemoryApprovalQueue(
            Options.Create(opts), new NoopApprovalExecutionRouter(), NullLogger<InMemoryApprovalQueue>.Instance);
        // Ön kontrol sahiplik doğruladığı için sipariş 1030'un gerçek sahibi (1027) olarak çağır.
        var contextAccessor = new ApprovalContextAccessor();
        using var scope = contextAccessor.SetScope("s1", null, "iptal", "1027");
        var svc = Build(opts, queue, contextAccessor);
        var fn = svc.BuildOrderCancelTool();

        var result = await fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["orderId"] = "1030",
            ["reason"] = "müşteri vazgeçti"
        }), TestContext.Current.CancellationToken);

        var (success, message) = ParseResult(result);
        success.Should().BeTrue();
        message.Should().Contain("onaya gönderildi");
        queue.GetPending().Should().ContainSingle(p => p.ToolName == WellKnown.ToolNames.OrderCancel);
    }

    [Fact]
    public async Task BuildReturnRequestTool_ApprovalRequired_ReturnsPendingWithoutExecutingTool()
    {
        var opts = new ApprovalOptions
        {
            Enabled = true,
            ToolsRequiringApproval = new() { WellKnown.ToolNames.ReturnRequest }
        };
        var queue = new InMemoryApprovalQueue(
            Options.Create(opts), new NoopApprovalExecutionRouter(), NullLogger<InMemoryApprovalQueue>.Instance);
        // Ön kontrol sahiplik doğruladığı için sipariş 1042'nin gerçek sahibi (1010) olarak çağır.
        var contextAccessor = new ApprovalContextAccessor();
        using var scope = contextAccessor.SetScope("s1", null, "iade", "1010");
        var svc = Build(opts, queue, contextAccessor);
        var fn = svc.BuildReturnRequestTool();

        var result = await fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["orderId"] = "1042",
            ["reason"] = "ürün kusurlu"
        }), TestContext.Current.CancellationToken);

        var (success, message) = ParseResult(result);
        success.Should().BeTrue();
        message.Should().Contain("onaya gönderildi");
        queue.GetPending().Should().ContainSingle(p => p.ToolName == WellKnown.ToolNames.ReturnRequest);
    }

    // ── Onay gerekmiyorsa tool doğrudan çalışır ─────────────────────────────────

    [Fact]
    public async Task BuildOrderPlacementTool_ApprovalDisabled_PassesThroughToTool()
    {
        var opts = new ApprovalOptions { Enabled = false };
        var contextAccessor = new ApprovalContextAccessor();
        using var scope = contextAccessor.SetScope("s1", null, "sipariş ver", "9007");
        var svc = Build(opts, contextAccessor: contextAccessor);
        var fn = svc.BuildOrderPlacementTool();

        var product = _fixture.ProductRepo.GetAll().First().Name;

        var result = await fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["lines"] = new[] { new OrderLineRequest(product, 1) }
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
        var contextAccessor = new ApprovalContextAccessor();
        using var scope = contextAccessor.SetScope("s1", null, "sipariş ver", "9008");
        var svc = Build(opts, contextAccessor: contextAccessor);
        var fn = svc.BuildOrderPlacementTool();
        var product = _fixture.ProductRepo.GetAll().First().Name;

        var result = await fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["lines"] = new[] { new OrderLineRequest(product, 1) }
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
            ["complaintText"] = $"şikayet metni unique {Guid.NewGuid()} buraya yazıldı"
        }), TestContext.Current.CancellationToken);
        var (success, _) = ParseResult(result);
        success.Should().BeTrue();
    }

    // ── Ön kontrol (preflight): baştan başarısız olacak talep onay kuyruğuna DÜŞMEMELİ ──
    // Gerçek iş admin kararından sonra çalıştığı için, sahiplik ihlali yürütme anında
    // yakalanırsa talep önce kuyruğa girer, admin'i meşgul eder, onaylanır ve ancak o zaman
    // sessizce başarısız olur. Ön kontrol bunu kayıt oluşturmadan önce keser.

    [Theory]
    [InlineData(WellKnown.ToolNames.OrderCancel)]
    [InlineData(WellKnown.ToolNames.ReturnRequest)]
    [InlineData(WellKnown.ToolNames.ComplaintRegistration)]
    public async Task ApprovalRequiredTools_ForeignOrder_RejectImmediatelyWithoutQueueing(string toolName)
    {
        var opts = new ApprovalOptions { Enabled = true, ToolsRequiringApproval = new() { toolName } };
        var queue = new InMemoryApprovalQueue(
            Options.Create(opts), new NoopApprovalExecutionRouter(), NullLogger<InMemoryApprovalQueue>.Instance);
        var contextAccessor = new ApprovalContextAccessor();

        // Sipariş 9701'e ait; saldırgan 9999 olarak login.
        var product = _fixture.ProductRepo.GetAll().First().Name;
        using (contextAccessor.SetScope("s0", null, "sipariş ver", "9701"))
        {
            var seeder = Build(new ApprovalOptions { Enabled = false }, contextAccessor: contextAccessor);
            await seeder.BuildOrderPlacementTool().InvokeAsync(new AIFunctionArguments(
                new Dictionary<string, object?> { ["lines"] = new[] { new OrderLineRequest(product, 1) } }),
                TestContext.Current.CancellationToken);
        }
        var foreignOrderId = _fixture.OrderRepo.GetByCustomer("9701").Last().OrderId;

        using var attacker = contextAccessor.SetScope("s1", null, "iptal et", "9999");
        var svc = Build(opts, queue, contextAccessor);
        var fn = toolName switch
        {
            WellKnown.ToolNames.OrderCancel => svc.BuildOrderCancelTool(),
            WellKnown.ToolNames.ReturnRequest => svc.BuildReturnRequestTool(),
            _ => svc.BuildComplaintRegistrationTool()
        };
        var args = toolName == WellKnown.ToolNames.ComplaintRegistration
            ? new Dictionary<string, object?> { ["orderId"] = foreignOrderId, ["complaintText"] = "başkasının siparişi hakkında şikayet" }
            : new Dictionary<string, object?> { ["orderId"] = foreignOrderId, ["reason"] = "başkasının siparişi" };

        var result = await fn.InvokeAsync(new AIFunctionArguments(args), TestContext.Current.CancellationToken);

        var (success, message) = ParseResult(result);
        success.Should().BeFalse("ön kontrol talebi reddetmeli");
        message.Should().NotContain("onaya gönderildi", "pending sonucu dönmemeli");
        queue.GetPending().Should().BeEmpty("admin kuyruğu baştan başarısız taleple kirletilmemeli");
    }

    // ── Salt-okunur sipariş tool'ları: customerId LLM'e hiç parametre olarak gösterilmez,
    //    ApprovalContext'ten (JWT'den) alınır — bkz. #A1 güvenlik düzeltmesi.

    [Fact]
    public async Task BuildOrderStatusTool_UsesContextCustomerId_NotLlmParameter()
    {
        var opts = new ApprovalOptions { Enabled = false };
        var product = _fixture.ProductRepo.GetAll().First().Name;
        var contextAccessor = new ApprovalContextAccessor();
        using var placeScope = contextAccessor.SetScope("s1", null, "sipariş ver", "9601");
        var svc = Build(opts, contextAccessor: contextAccessor);
        var placed = await svc.BuildOrderPlacementTool().InvokeAsync(new AIFunctionArguments(
            new Dictionary<string, object?> { ["lines"] = new[] { new OrderLineRequest(product, 1) } }),
            TestContext.Current.CancellationToken);
        var orderId = ((JsonElement)placed!).GetProperty("data").GetProperty("orderId").GetString();

        // Aynı context (customerId=9601) ile sorgulanırsa bulunmalı.
        var ownFn = svc.BuildOrderStatusTool();
        var ownResult = await ownFn.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["orderId"] = orderId }),
            TestContext.Current.CancellationToken);
        ParseResult(ownResult).success.Should().BeTrue();

        // Başka bir müşterinin context'i ile aynı sipariş sorgulanırsa NotFound (mismatch sızdırmaz).
        using var otherScope = contextAccessor.SetScope("s2", null, "sorgula", "9999");
        var otherFn = svc.BuildOrderStatusTool();
        var otherResult = await otherFn.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["orderId"] = orderId }),
            TestContext.Current.CancellationToken);
        ParseResult(otherResult).success.Should().BeFalse();
    }

    [Fact]
    public async Task BuildGetLastOrderTool_TakesNoParameters_UsesContextCustomerId()
    {
        var opts = new ApprovalOptions { Enabled = false };
        var product = _fixture.ProductRepo.GetAll().First().Name;
        var contextAccessor = new ApprovalContextAccessor();
        using var scope = contextAccessor.SetScope("s1", null, "sipariş ver", "9602");
        var svc = Build(opts, contextAccessor: contextAccessor);
        await svc.BuildOrderPlacementTool().InvokeAsync(new AIFunctionArguments(
            new Dictionary<string, object?> { ["lines"] = new[] { new OrderLineRequest(product, 1) } }),
            TestContext.Current.CancellationToken);

        var fn = svc.BuildGetLastOrderTool();
        var result = await fn.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?>()), TestContext.Current.CancellationToken);
        ParseResult(result).success.Should().BeTrue();
    }

    [Fact]
    public async Task BuildOrderCancelTool_ApprovalDisabled_RejectsWhenOrderBelongsToDifferentCustomer()
    {
        var opts = new ApprovalOptions { Enabled = false };
        var product = _fixture.ProductRepo.GetAll().First().Name;
        var contextAccessor = new ApprovalContextAccessor();
        using var placeScope = contextAccessor.SetScope("s1", null, "sipariş ver", "9603");
        var svc = Build(opts, contextAccessor: contextAccessor);
        var placed = await svc.BuildOrderPlacementTool().InvokeAsync(new AIFunctionArguments(
            new Dictionary<string, object?> { ["lines"] = new[] { new OrderLineRequest(product, 1) } }),
            TestContext.Current.CancellationToken);
        var orderId = ((JsonElement)placed!).GetProperty("data").GetProperty("orderId").GetString();

        // Başka bir müşterinin context'i ile aynı siparişi iptal etmeye çalış.
        using var attackerScope = contextAccessor.SetScope("s2", null, "iptal et", "9999");
        var fn = svc.BuildOrderCancelTool();
        var result = await fn.InvokeAsync(new AIFunctionArguments(
            new Dictionary<string, object?> { ["orderId"] = orderId, ["reason"] = "başkasının siparişi" }),
            TestContext.Current.CancellationToken);

        ParseResult(result).success.Should().BeFalse();
        _fixture.OrderRepo.Get(orderId!)!.Status.Should().NotBe(WellKnown.OrderStatuses.Cancelled);
    }

    // ── RequestApprovalAsync — WorkflowRunner'ın RequestInfoEvent köprüsünden çağırdığı metot ──
    // (Bu 4 tool artık bu yolu kullanmıyor, ama metod başka onay senaryoları için hâlâ var.)

    [Fact]
    public async Task RequestApprovalAsync_Rejected_ReturnsApprovedFalseWithReason()
    {
        var opts = new ApprovalOptions { Enabled = true, TimeoutSeconds = 5 };
        var queue = new InMemoryApprovalQueue(
            Options.Create(opts),
            new NoopApprovalExecutionRouter(), NullLogger<InMemoryApprovalQueue>.Instance);
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
            new NoopApprovalExecutionRouter(), NullLogger<InMemoryApprovalQueue>.Instance);
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
            new NoopApprovalExecutionRouter(), NullLogger<InMemoryApprovalQueue>.Instance);
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

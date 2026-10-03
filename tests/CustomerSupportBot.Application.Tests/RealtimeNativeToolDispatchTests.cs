// Sesli görüşmede tool çalıştırma — yazılı sohbetin iş tool'larının TAMAMI.
//
// Eskiden sesli kanal yalnızca 5 okuma tool'unu çalıştırabiliyordu; sipariş, iptal, iade ve
// şikayet kaydı "FORBIDDEN_IN_VOICE" ile reddediliyor, okuma yapan şikayet tool'ları hiç
// tanımlı değildi. Yan etkili işlemler artık yazılı sohbetle AYNI onay kapısından geçer:
// işlem yapılmaz, insan onayı için kayıt oluşturulur.

using System.Text.Json;
using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Approval;
using CustomerSupportBot.Application.Services.Providers;
using CustomerSupportBot.Application.Services.Realtime;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class RealtimeNativeToolDispatchTests
{
    private const string CustomerId = "1001";

    private sealed class Harness
    {
        public required RealtimeNativeService Service { get; init; }
        public required InMemoryApprovalQueue Approvals { get; init; }
        public required InMemoryEscalationSink Escalations { get; init; }
        public required IOrderRepository Orders { get; init; }
        public AgentSession Session { get; } = new()
        {
            SessionId = "voice-tools",
            State = { AuthenticatedCustomerId = CustomerId }
        };

        public async Task<JsonElement> CallAsync(string tool, string argsJson, string? userQuery = "sesli talep")
        {
            var json = await Service.DispatchToolAsync(tool, argsJson, Session, userQuery);
            return JsonDocument.Parse(json).RootElement;
        }
    }

    private static Harness Build(bool withGate = true)
    {
        var options = Options.Create(new ApprovalOptions());
        var approvals = new InMemoryApprovalQueue(
            options, Substitute.For<IApprovalExecutionRouter>(), NullLogger<InMemoryApprovalQueue>.Instance);
        var escalations = new InMemoryEscalationSink(NullLogger<InMemoryEscalationSink>.Instance);
        var orders = Substitute.For<IOrderRepository>();
        var locks = new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 }));

        var service = new RealtimeNativeService(
            Substitute.For<IRealtimeVoiceTransport>(),
            new InMemorySessionManager(locks),
            TestFactory.CreateToolsService(
                Substitute.For<IProductCatalogRepository>(), orders, Substitute.For<IComplaintRepository>()),
            Substitute.For<IInputGuard>(),
            Substitute.For<IChatBridge>(),
            new CustomerIdentityHintBuilder(Substitute.For<ICustomerRepository>()),
            locks,
            NullLogger<RealtimeNativeService>.Instance,
            escalations: escalations,
            approvalGate: withGate ? new SideEffectApprovalGate(approvals, options) : null);

        return new Harness { Service = service, Approvals = approvals, Escalations = escalations, Orders = orders };
    }

    private static string? ErrorCode(JsonElement result) =>
        result.TryGetProperty("error", out var e) && e.TryGetProperty("code", out var c) ? c.GetString() : null;

    [Fact]
    public async Task OrderPlacement_ByVoice_GoesToApproval_InsteadOfExecuting()
    {
        var h = Build();

        var result = await h.CallAsync(WellKnown.ToolNames.OrderPlacement,
            """{"lines":[{"product_name":"Çay","quantity":2}]}""", userQuery: "iki çay sipariş et");

        result.GetProperty("pendingApproval").GetBoolean().Should().BeTrue("işlem hemen yapılmaz, onaya gider");
        var pending = h.Approvals.GetPending().Should().ContainSingle().Subject;
        pending.ToolName.Should().Be(WellKnown.ToolNames.OrderPlacement);
        pending.SessionId.Should().Be(h.Session.SessionId);
        pending.CustomerId.Should().Be(CustomerId, "müşteri kimliği modelden değil oturumdan gelir");
        pending.UserQuery.Should().Be("iki çay sipariş et");
        pending.Parameters.Should().ContainKey("lines").And.ContainKey("customerId");
    }

    [Theory]
    [InlineData("order_cancel_tool", """{"order_id":"1030","reason":"vazgeçtim"}""")]
    [InlineData("return_request_tool", """{"order_id":"1030","reason":"kusurlu ürün"}""")]
    [InlineData("complaint_registration_tool", """{"order_id":"1030","complaint_text":"ürün hasarlı geldi, kutusu ezik"}""")]
    public async Task OrderActions_ForAnUnknownOrder_AreRejectedBeforeReachingTheQueue(string tool, string args)
    {
        var h = Build();   // sipariş deposu boş — ön kontrol "bulunamadı" der

        var result = await h.CallAsync(tool, args);

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        h.Approvals.GetPending().Should().BeEmpty("baştan başarısız olacağı belli talep admin kuyruğuna düşmemeli");
    }

    [Fact]
    public async Task IncompleteOrderLines_AreRejectedBeforeReachingTheQueue()
    {
        var h = Build();

        var result = await h.CallAsync(WellKnown.ToolNames.OrderPlacement,
            """{"lines":[{"product_name":"Çay","quantity":0}]}""");

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        h.Approvals.GetPending().Should().BeEmpty();
    }

    [Theory]
    [InlineData("complaint_status_tool", """{"complaint_id":"1001"}""")]
    [InlineData("get_all_complaints_tool", "{}")]
    public async Task ComplaintReadTools_AreAvailableInVoice(string tool, string args)
    {
        var h = Build();

        var result = await h.CallAsync(tool, args);

        ErrorCode(result).Should().NotBe("UNKNOWN_TOOL");
    }

    [Fact]
    public async Task HumanHandoff_OpensATicketForAHumanAgent()
    {
        var h = Build();

        var result = await h.CallAsync(WellKnown.ToolNames.HumanHandoff,
            """{"reason":"Bir temsilciyle görüşmek istiyorum"}""", userQuery: "temsilciye bağlar mısın");

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        var ticket = h.Escalations.GetOpen().Should().ContainSingle().Subject;
        ticket.SessionId.Should().Be(h.Session.SessionId);
        ticket.AgentName.Should().Be(WellKnown.AgentNames.HumanHandoff);
        ticket.UserQuery.Should().Be("temsilciye bağlar mısın");
    }

    [Fact]
    public async Task NumericArguments_AreAccepted()
    {
        var h = Build();

        // Model sipariş numarasını bazen sayı olarak gönderir; eskiden bu TOOL_DISPATCH_ERROR'du.
        var act = () => h.CallAsync(WellKnown.ToolNames.OrderStatus, """{"order_id":1030}""");

        (await act.Should().NotThrowAsync()).Subject.ValueKind.Should().Be(JsonValueKind.Object);
        h.Orders.ReceivedCalls().Should().NotBeEmpty("sipariş numarası okunup depoya sorulmalı");
    }

    [Fact]
    public async Task WithoutApprovalGate_SideEffectsAreNotExecuted()
    {
        var h = Build(withGate: false);

        var result = await h.CallAsync(WellKnown.ToolNames.OrderPlacement,
            """{"lines":[{"product_name":"Çay","quantity":1}]}""");

        ErrorCode(result).Should().Be("APPROVAL_UNAVAILABLE",
            "onay kapısı yoksa yan etkili işlem ASLA doğrudan çalışmamalı");
    }
}

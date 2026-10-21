// Sipariş/şikayet araçları başarılı olunca, sohbet ekranının çizeceği YAPILANDIRILMIŞ kartı (ui_hint) de yayınlar.
// Kartlar veritabanındaki gerçek veriden gelir (modelin metninden değil); iç alanlar (iç not, temsilci, onay) kartta yok.

using System.Text.Json;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Tools;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Tests;

public class ToolCardHintTests
{
    private static readonly JsonSerializerOptions Relaxed =
        new(JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };   // SSE yazıcısıyla aynı: camelCase

    private sealed class Rig
    {
        public IUiHintEmitter Hints { get; } = Substitute.For<IUiHintEmitter>();
        public IOrderRepository Orders { get; } = Substitute.For<IOrderRepository>();
        public IComplaintRepository Complaints { get; } = Substitute.For<IComplaintRepository>();
        public OrderToolsService OrderTools { get; }
        public ComplaintToolsService ComplaintTools { get; }
        public List<JsonElement> Emitted { get; } = [];

        public Rig(bool withEmitter = true)
        {
            Hints.Emit(Arg.Any<StreamEvent>()).Returns(call =>
            {
                Emitted.Add(JsonSerializer.SerializeToElement(call.Arg<StreamEvent>().Data, Relaxed));
                return true;
            });
            var emitter = withEmitter ? Hints : null;
            OrderTools = new(Orders, Substitute.For<IProductCatalogRepository>(), Substitute.For<ICustomerRepository>(), null, emitter);
            ComplaintTools = new(Complaints, Orders, null, emitter);
        }
    }

    private static OrderInfo Shipped(string customer = "1001") => new()
    {
        CustomerId = customer, Status = WellKnown.OrderStatuses.Shipped, Lines = [new OrderLine("Kahve", 2), new OrderLine("Çay", 1)],
        OrderDate = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc), ShippedAt = new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc),
        Carrier = "Yurtiçi Kargo", TrackingNumber = "YK123456"
    };

    // ═══ sipariş kartları ═══

    [Fact]
    public void OrderStatus_Success_EmitsOneOrderCard()
    {
        var rig = new Rig();
        rig.Orders.Get("1044").Returns(Shipped());

        var result = rig.OrderTools.OrderStatusTool("1044", "1001");

        result.Success.Should().BeTrue();
        var hint = rig.Emitted.Should().ContainSingle().Subject;
        hint.GetProperty("kind").GetString().Should().Be("order_cards");
        var card = hint.GetProperty("orders").EnumerateArray().Single();
        card.GetProperty("orderId").GetString().Should().Be("1044");
        card.GetProperty("status").GetString().Should().Be("Kargolandı");
        card.GetProperty("carrier").GetString().Should().Be("Yurtiçi Kargo");
        card.GetProperty("trackingNumber").GetString().Should().Be("YK123456");
        card.GetProperty("totalQuantity").GetInt32().Should().Be(3);
        card.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("product").GetString()).Should().Equal("Kahve", "Çay");
    }

    [Fact]
    public void OrderStatus_ForSomeoneElsesOrder_EmitsNothing()
    {
        var rig = new Rig();
        rig.Orders.Get("1044").Returns(Shipped(customer: "9999"));

        var result = rig.OrderTools.OrderStatusTool("1044", "1001");

        result.Success.Should().BeFalse();
        rig.Emitted.Should().BeEmpty("başkasının siparişi kart olarak da sızmamalı");
    }

    [Fact]
    public void OrderStatus_NotFound_EmitsNothing()
    {
        var rig = new Rig();

        rig.OrderTools.OrderStatusTool("yok", "1001");

        rig.Emitted.Should().BeEmpty();
    }

    [Fact]
    public void LastOrder_EmitsACard_AndNoOrdersEmitsNone()
    {
        var rig = new Rig();
        rig.Orders.GetLast("1001").Returns(("1044", Shipped()));

        rig.OrderTools.GetLastOrderTool("1001");
        rig.OrderTools.GetLastOrderTool("2002");

        rig.Emitted.Should().ContainSingle();
    }

    [Fact]
    public void AllOrders_EmitsNewestFive_WithTheTotalCount()
    {
        var rig = new Rig();
        var orders = Enumerable.Range(1, 8).Select(i =>
            ($"10{i:00}", new OrderInfo { CustomerId = "1001", Status = "İşleniyor", Lines = [new OrderLine("Kahve", 1)], OrderDate = new DateTime(2026, 10, i, 0, 0, 0, DateTimeKind.Utc) }))
            .ToList();
        rig.Orders.GetByCustomer("1001").Returns(orders);

        rig.OrderTools.GetAllOrdersTool("1001");

        var hint = rig.Emitted.Should().ContainSingle().Subject;
        hint.GetProperty("totalCount").GetInt32().Should().Be(8);
        hint.GetProperty("orders").EnumerateArray().Select(o => o.GetProperty("orderId").GetString())
            .Should().Equal("1008", "1007", "1006", "1005", "1004");
    }

    [Fact]
    public void WithoutAnEmitter_TheToolsStillWork()
    {
        var rig = new Rig(withEmitter: false);
        rig.Orders.Get("1044").Returns(Shipped());

        rig.OrderTools.OrderStatusTool("1044", "1001").Success.Should().BeTrue();
    }

    [Fact]
    public void WhenTheChannelCannotShowCards_TheToolResultIsUnchanged()
    {
        var rig = new Rig();
        rig.Hints.Emit(Arg.Any<StreamEvent>()).Returns(false);   // ör. sesli kanal: ambient akış yok
        rig.Orders.Get("1044").Returns(Shipped());

        rig.OrderTools.OrderStatusTool("1044", "1001").Success.Should().BeTrue();
    }

    // ═══ şikayet kartları ═══

    [Fact]
    public void ComplaintStatus_EmitsACard_WithoutInternalFields()
    {
        var rig = new Rig();
        rig.Complaints.Get("1006").Returns(new ComplaintInfo
        {
            OrderId = "1030", CustomerId = "1001", Complaint = "ürün kırık geldi", Status = WellKnown.ComplaintStatuses.Resolved,
            Resolution = "Yeni ürün gönderildi.", AssignedAgentId = "agent-7", ApprovalId = "appr-1",
            CreatedAt = new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc), UpdatedAt = new DateTime(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc)
        });

        rig.ComplaintTools.ComplaintStatusTool("1006", "1001");

        var hint = rig.Emitted.Should().ContainSingle().Subject;
        hint.GetProperty("kind").GetString().Should().Be("complaint_cards");
        var card = hint.GetProperty("complaints").EnumerateArray().Single();
        card.GetProperty("complaintId").GetString().Should().Be("1006");
        card.GetProperty("orderId").GetString().Should().Be("1030");
        card.GetProperty("status").GetString().Should().Be("Çözüldü");
        card.GetProperty("resolution").GetString().Should().Be("Yeni ürün gönderildi.");
        hint.GetRawText().Should().NotContain("agent-7").And.NotContain("appr-1");
    }

    [Fact]
    public void ComplaintStatus_Open_HidesAnyResolutionDraft()
    {
        var rig = new Rig();
        rig.Complaints.Get("1006").Returns(new ComplaintInfo
        {
            OrderId = "1030", CustomerId = "1001", Complaint = "kırık", Status = WellKnown.ComplaintStatuses.InProgress,
            Resolution = "taslak not", UpdatedAt = DateTime.UtcNow
        });

        rig.ComplaintTools.ComplaintStatusTool("1006", "1001");

        rig.Emitted.Single().GetProperty("complaints").EnumerateArray().Single().GetProperty("resolution").ValueKind
            .Should().Be(JsonValueKind.Null, "açıklama yalnızca kapanmış şikayette müşteriye gösterilir");
    }

    [Fact]
    public void ComplaintStatus_ForSomeoneElsesComplaint_EmitsNothing()
    {
        var rig = new Rig();
        rig.Complaints.Get("1006").Returns(new ComplaintInfo { OrderId = "1030", CustomerId = "9999", Complaint = "x", Status = "Beklemede" });

        rig.ComplaintTools.ComplaintStatusTool("1006", "1001");

        rig.Emitted.Should().BeEmpty();
    }

    [Fact]
    public void AllComplaints_EmitsACardPerComplaint()
    {
        var rig = new Rig();
        rig.Complaints.GetByCustomer("1001").Returns(new List<(string, ComplaintInfo)>
        {
            ("1006", new ComplaintInfo { OrderId = "1030", CustomerId = "1001", Complaint = "a", Status = "Beklemede" }),
            ("1007", new ComplaintInfo { OrderId = "1031", CustomerId = "1001", Complaint = "b", Status = "İncelemede" })
        });

        rig.ComplaintTools.GetAllComplaintsTool("1001");

        rig.Emitted.Single().GetProperty("complaints").GetArrayLength().Should().Be(2);
    }
}

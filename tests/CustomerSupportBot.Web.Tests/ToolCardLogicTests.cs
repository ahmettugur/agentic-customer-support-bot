// Sohbetteki sipariş/şikayet kartlarının saf mantığı: zaman çizelgesi, durum tonu, elle biçimlenen tarih.
// Sunucu kartı ui_hint ile yollar; JSON şekli de burada çivilenir (alan adı kayarsa kart sessizce boş çizilirdi).

using System.Text.Json;
using CustomerSupportBot.Web.Services;

namespace CustomerSupportBot.Web.Tests;

public class ToolCardLogicTests
{
    private static readonly DateTimeOffset Ordered = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Shipped = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Delivered = new(2026, 10, 7, 15, 0, 0, TimeSpan.Zero);

    private static OrderCardView Order(string status, DateTimeOffset? shipped = null, DateTimeOffset? delivered = null, DateTimeOffset? cancelled = null) =>
        new("1044", status, Ordered, [new("Kahve", 2)], 2, null, null, shipped, delivered, cancelled);

    // ═══ zaman çizelgesi ═══

    [Fact]
    public void Processing_HasOneCurrentStep_AndTwoPending()
    {
        var steps = ToolCardLogic.Steps(Order("İşleniyor"), TimeSpan.Zero);

        steps.Select(s => (s.Label, s.State)).Should().Equal(
            ("Sipariş alındı", CardStepState.Current), ("Kargolandı", CardStepState.Pending), ("Teslim edildi", CardStepState.Pending));
    }

    [Fact]
    public void Shipped_MarksTheFirstDone_AndShippingCurrent_WithDates()
    {
        var steps = ToolCardLogic.Steps(Order("Kargolandı", shipped: Shipped), TimeSpan.Zero);

        steps.Select(s => s.State).Should().Equal(CardStepState.Done, CardStepState.Current, CardStepState.Pending);
        steps[0].When.Should().Be("03.10.2026");
        steps[1].When.Should().Be("05.10.2026");
        steps[2].When.Should().BeNull();
    }

    [Fact]
    public void Delivered_IsAllDone()
    {
        var steps = ToolCardLogic.Steps(Order("Teslim Edildi", Shipped, Delivered), TimeSpan.Zero);

        steps.Should().OnlyContain(s => s.State == CardStepState.Done);
        steps[2].When.Should().Be("07.10.2026");
    }

    [Fact]
    public void Cancelled_ShowsOnlyReceivedThenCancelled()
    {
        var steps = ToolCardLogic.Steps(Order("İptal Edildi", cancelled: new(2026, 10, 4, 10, 0, 0, TimeSpan.Zero)), TimeSpan.Zero);

        steps.Select(s => s.Label).Should().Equal("Sipariş alındı", "İptal edildi");
        steps[1].State.Should().Be(CardStepState.Failed);
        steps[1].When.Should().Be("04.10.2026");
    }

    [Theory]
    [InlineData("İade Talep Edildi", "İade talep edildi", CardStepState.Current)]
    [InlineData("İade Onaylandı", "İade onaylandı", CardStepState.Done)]
    public void Returns_AppendAStepAfterDelivery(string status, string label, CardStepState state)
    {
        var steps = ToolCardLogic.Steps(Order(status, Shipped, Delivered), TimeSpan.Zero);

        steps.Should().HaveCount(4);
        steps.Take(3).Should().OnlyContain(s => s.State == CardStepState.Done);
        (steps[3].Label, steps[3].State).Should().Be((label, state));
    }

    [Fact]
    public void UnknownStatus_FallsBackToASingleCurrentStep_NeverThrows()
    {
        var steps = ToolCardLogic.Steps(Order("Beklenmeyen"), TimeSpan.Zero);

        steps.Should().ContainSingle().Which.State.Should().Be(CardStepState.Current);
    }

    // ═══ ton ve tarih ═══

    [Theory]
    [InlineData("İşleniyor", "info")]
    [InlineData("Kargolandı", "progress")]
    [InlineData("Teslim Edildi", "success")]
    [InlineData("İptal Edildi", "danger")]
    [InlineData("İade Talep Edildi", "warn")]
    [InlineData("İade Onaylandı", "warn")]
    [InlineData("Başka", "neutral")]
    public void OrderTone(string status, string tone) => ToolCardLogic.OrderTone(status).Should().Be(tone);

    [Theory]
    [InlineData("Beklemede", "warn")]
    [InlineData("İncelemede", "info")]
    [InlineData("Çözüldü", "success")]
    [InlineData("Reddedildi", "danger")]
    [InlineData("Başka", "neutral")]
    public void ComplaintTone(string status, string tone) => ToolCardLogic.ComplaintTone(status).Should().Be(tone);

    [Fact]
    public void Date_IsFormattedByHand_InTheBrowsersOffset()
    {
        // 22:30 UTC, tarayıcı UTC+3 → yerelde ertesi gün.
        ToolCardLogic.Date(new DateTimeOffset(2026, 10, 3, 22, 30, 0, TimeSpan.Zero), TimeSpan.FromHours(3)).Should().Be("04.10.2026");
    }

    [Fact]
    public void Tracking_JoinsCarrierAndNumber_WhenPresent()
    {
        ToolCardLogic.Shipping(new("1", "Kargolandı", Ordered, [], 0, "Yurtiçi Kargo", "YK1", null, null, null)).Should().Be("Yurtiçi Kargo · YK1");
        ToolCardLogic.Shipping(new("1", "Kargolandı", Ordered, [], 0, null, "YK1", null, null, null)).Should().Be("YK1");
        ToolCardLogic.Shipping(new("1", "Kargolandı", Ordered, [], 0, "Yurtiçi Kargo", null, null, null, null)).Should().Be("Yurtiçi Kargo");
        ToolCardLogic.Shipping(new("1", "İşleniyor", Ordered, [], 0, null, null, null, null, null)).Should().BeNull();
    }

    // ═══ sunucu sözleşmesi ═══

    [Fact]
    public void OrderCards_DeserializeTheHintTheServerEmits()
    {
        const string json = """
            {"kind":"order_cards","totalCount":8,"orders":[{"orderId":"1044","status":"Kargolandı","orderDate":"2026-10-03T12:00:00Z",
             "lines":[{"product":"Kahve","quantity":2}],"totalQuantity":2,"carrier":"Yurtiçi Kargo","trackingNumber":"YK123456",
             "shippedAt":"2026-10-05T09:00:00Z","deliveredAt":null,"cancelledAt":null}]}
            """;

        var hint = ToolCardLogic.ParseOrderHint(JsonSerializer.Deserialize<JsonElement>(json));

        hint.Should().NotBeNull();
        hint!.TotalCount.Should().Be(8);
        hint.Orders.Single().Should().BeEquivalentTo(new OrderCardView("1044", "Kargolandı", Ordered, [new("Kahve", 2)], 2,
            "Yurtiçi Kargo", "YK123456", Shipped, null, null));
    }

    [Fact]
    public void ComplaintCards_DeserializeTheHintTheServerEmits()
    {
        const string json = """
            {"kind":"complaint_cards","complaints":[{"complaintId":"1006","orderId":"1030","status":"Çözüldü","complaint":"kırık",
             "resolution":"Yeni ürün gönderildi.","updatedAt":"2026-10-12T08:00:00Z"}]}
            """;

        var cards = ToolCardLogic.ParseComplaintHint(JsonSerializer.Deserialize<JsonElement>(json));

        cards.Single().Resolution.Should().Be("Yeni ürün gönderildi.");
        cards.Single().ComplaintId.Should().Be("1006");
    }

    [Theory]
    [InlineData("""{"kind":"order_cards"}""")]
    [InlineData("""{"kind":"order_cards","orders":"yanlış"}""")]
    [InlineData("""{}""")]
    public void MalformedHints_AreIgnored_NotThrown(string json) =>
        ToolCardLogic.ParseOrderHint(JsonSerializer.Deserialize<JsonElement>(json)).Should().BeNull();
}

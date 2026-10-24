// İptal/iade kuralları TEK yerde (OrderPolicy): hem araçlar hem depo hem "iade edebilir miyim?" sorusu aynı kararı verir.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Domain.Tests;

public class OrderPolicyTests
{
    private static readonly DateTime Now = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    private static OrderInfo Order(string status, DateTime? delivered = null, DateTime? ordered = null) => new()
    {
        Status = status, DeliveredAt = delivered, OrderDate = ordered ?? Now.AddDays(-30)
    };

    [Theory]
    [InlineData("İşleniyor")]
    [InlineData("Kargolandı")]
    public void CancelIsAllowed_WhileProcessingOrShipped(string status)
    {
        var d = OrderPolicy.EvaluateCancel(Order(status));

        d.Allowed.Should().BeTrue();
        d.Code.Should().Be(OrderPolicy.Eligible);
    }

    [Fact]
    public void CancelIsRefused_ForADeliveredOrder()
    {
        var d = OrderPolicy.EvaluateCancel(Order(WellKnown.OrderStatuses.Delivered));

        d.Allowed.Should().BeFalse();
        d.Code.Should().Be(WellKnown.ToolErrorCodes.OrderNotCancellable);
        d.Message.Should().Contain("Teslim Edildi").And.Contain("iade");
    }

    [Fact]
    public void CancelIsRefused_ForAnAlreadyCancelledOrder() =>
        OrderPolicy.EvaluateCancel(Order(WellKnown.OrderStatuses.Cancelled)).Code
            .Should().Be(WellKnown.ToolErrorCodes.OrderAlreadyCancelled);

    [Fact]
    public void ReturnIsAllowed_WithinTheWindow_AndSaysUntilWhen()
    {
        var d = OrderPolicy.EvaluateReturn(Order(WellKnown.OrderStatuses.Delivered, Now.AddDays(-4)), Now);

        d.Allowed.Should().BeTrue();
        d.DaysLeft.Should().Be(10);
        d.DeadlineUtc.Should().Be(Now.AddDays(-4).AddDays(14));
        d.Message.Should().Contain("03.11.2026").And.Contain("10 gün");
    }

    [Fact]
    public void TheWindowIsInclusiveOfTheLastDay()
    {
        OrderPolicy.EvaluateReturn(Order(WellKnown.OrderStatuses.Delivered, Now.AddDays(-14)), Now).Allowed.Should().BeTrue();
        OrderPolicy.EvaluateReturn(Order(WellKnown.OrderStatuses.Delivered, Now.AddDays(-14).AddMinutes(-1)), Now).Allowed.Should().BeFalse();
    }

    [Fact]
    public void AnExpiredWindow_NamesTheDeadline_InsteadOfGuessing()
    {
        var d = OrderPolicy.EvaluateReturn(Order(WellKnown.OrderStatuses.Delivered, Now.AddDays(-20)), Now);

        d.Allowed.Should().BeFalse();
        d.Code.Should().Be(WellKnown.ToolErrorCodes.ReturnNotEligible);
        d.Message.Should().Contain("14 günlük").And.Contain("18.10.2026").And.Contain("doldu").And.NotContain("olabilir");
    }

    [Fact]
    public void WithoutADeliveryDate_TheOrderDateIsTheConservativeStart()
    {
        var d = OrderPolicy.EvaluateReturn(Order(WellKnown.OrderStatuses.Delivered, delivered: null, ordered: Now.AddDays(-20)), Now);

        d.Allowed.Should().BeFalse();
        OrderPolicy.ReturnWindowStart(null, Now.AddDays(-20)).Should().Be(Now.AddDays(-20));
    }

    [Theory]
    [InlineData("İşleniyor")]
    [InlineData("Kargolandı")]
    public void ReturnIsRefused_UntilDelivered(string status)
    {
        var d = OrderPolicy.EvaluateReturn(Order(status), Now);

        d.Allowed.Should().BeFalse();
        d.Message.Should().Contain(status).And.Contain("teslim");
    }

    [Theory]
    [InlineData("İade Talep Edildi")]
    [InlineData("İade Onaylandı")]
    public void ReturnIsRefused_WhenOneAlreadyExists(string status) =>
        OrderPolicy.EvaluateReturn(Order(status), Now).Code.Should().Be(WellKnown.ToolErrorCodes.ReturnAlreadyRequested);

    [Fact]
    public void ReturnIsRefused_ForACancelledOrder() =>
        OrderPolicy.EvaluateReturn(Order(WellKnown.OrderStatuses.Cancelled), Now).Allowed.Should().BeFalse();

    [Fact]
    public void Evaluate_DispatchesByAction()
    {
        var o = Order(WellKnown.OrderStatuses.Delivered, Now.AddDays(-1));

        OrderPolicy.Evaluate(OrderAction.Return, o, Now).Allowed.Should().BeTrue();
        OrderPolicy.Evaluate(OrderAction.Cancel, o, Now).Allowed.Should().BeFalse();
    }
}

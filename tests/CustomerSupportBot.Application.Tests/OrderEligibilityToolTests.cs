// "İade/iptal edebilir miyim?" — salt-okunur, kural motorundan; iade başarısızlık mesajı da aynı kaynaktan.

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Tools;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Tests;

public class OrderEligibilityToolTests
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static (OrderToolsService Svc, IOrderRepository Orders) Build(OrderInfo order)
    {
        var orders = Substitute.For<IOrderRepository>();
        orders.Get("1044").Returns(order);
        var svc = new OrderToolsService(orders, Substitute.For<IProductCatalogRepository>(), Substitute.For<ICustomerRepository>(),
            clock: new FixedClock(Now));
        return (svc, orders);
    }

    private static OrderInfo Delivered(int daysAgo, string customer = "1001") => new()
    {
        CustomerId = customer, Status = WellKnown.OrderStatuses.Delivered, Lines = [new OrderLine("Kahve", 1)],
        DeliveredAt = Now.UtcDateTime.AddDays(-daysAgo), OrderDate = Now.UtcDateTime.AddDays(-daysAgo - 3)
    };

    [Fact]
    public void WithinTheWindow_SaysYesAndUntilWhen()
    {
        var (svc, orders) = Build(Delivered(4));

        var r = svc.OrderEligibilityTool("1044", "return", "1001");

        r.Success.Should().BeTrue();
        r.Message.Should().Contain("03.11.2026");
        orders.DidNotReceive().RequestReturn(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public void AfterTheWindow_SaysNo_AsAnAnswerNotAnError()
    {
        var (svc, _) = Build(Delivered(20));

        var r = svc.OrderEligibilityTool("1044", "return", "1001");

        r.Success.Should().BeTrue("müşterinin sorusuna verilen geçerli bir cevap; hata değil");
        r.Message.Should().Contain("doldu").And.Contain("18.10.2026");
    }

    [Theory]
    [InlineData("iptal")]
    [InlineData("cancel")]
    public void CancelAliases_AreUnderstood(string action)
    {
        var o = Delivered(1);
        o.Status = WellKnown.OrderStatuses.Processing;
        var (svc, _) = Build(o);

        svc.OrderEligibilityTool("1044", action, "1001").Message.Should().Contain("iptal edilebilir");
    }

    [Fact]
    public void AnUnknownAction_IsAValidationError()
    {
        var (svc, _) = Build(Delivered(1));

        svc.OrderEligibilityTool("1044", "değişim", "1001").Success.Should().BeFalse();
    }

    [Fact]
    public void AnotherCustomersOrder_IsRefused_AndNeverEvaluated()
    {
        var (svc, _) = Build(Delivered(4, customer: "2002"));

        var r = svc.OrderEligibilityTool("1044", "return", "1001");

        r.Success.Should().BeFalse();
        r.Message.Should().NotContain("03.11.2026");
    }

    [Fact]
    public void WhenTheReturnIsRefusedAtExecution_TheMessageStatesTheRealReason()
    {
        var (svc, orders) = Build(Delivered(20));
        orders.RequestReturn("1044", Arg.Any<string>()).Returns(false);

        var r = svc.ReturnRequestTool("1044", "kutu ezik geldi", "1001");

        r.Success.Should().BeFalse();
        r.Message.Should().Contain("18.10.2026").And.NotContain("olabilir");
    }
}

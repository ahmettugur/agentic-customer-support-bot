// Bot, kargoya verilmiş siparişin kargo firmasını ve takip numarasını söyleyebilmeli.

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Tools;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Tests;

public class OrderStatusToolShippingTests
{
    private static OrderToolsService Build(OrderInfo order)
    {
        var orders = Substitute.For<IOrderRepository>();
        orders.Get("1044").Returns(order);
        orders.GetLast("1001").Returns(("1044", order));
        return new OrderToolsService(orders, Substitute.For<IProductCatalogRepository>(), Substitute.For<ICustomerRepository>());
    }

    private static OrderInfo Shipped => new()
    {
        CustomerId = "1001", Status = WellKnown.OrderStatuses.Shipped, Lines = [new OrderLine("Kahve", 1)],
        Carrier = "Yurtiçi Kargo", TrackingNumber = "YK123456", ShippedAt = new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public void OrderStatus_IncludesCarrierAndTracking()
    {
        var r = Build(Shipped).OrderStatusTool("1044", "1001");

        r.Message.Should().Contain("Kargo: Yurtiçi Kargo").And.Contain("Takip No: YK123456");
    }

    [Fact]
    public void LastOrder_IncludesCarrierAndTracking()
    {
        Build(Shipped).GetLastOrderTool("1001").Message.Should().Contain("Takip No: YK123456");
    }

    [Fact]
    public void NotShipped_HasNoShippingText()
    {
        var order = Shipped;
        order.Status = WellKnown.OrderStatuses.Processing;
        order.Carrier = order.TrackingNumber = null;

        Build(order).OrderStatusTool("1044", "1001").Message.Should().NotContain("Kargo:").And.NotContain("Takip");
    }
}

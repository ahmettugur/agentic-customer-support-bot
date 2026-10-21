// Bildirim metinlerinin üretimi (saf) + yaşam döngüsü servislerinin yayını.

using CustomerSupportBot.Application.Services.Notifications;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Tests;

public class CustomerNotificationsTests
{
    [Fact]
    public void Shipped_MentionsTheOrderCarrierAndTracking()
    {
        var order = new OrderInfo { CustomerId = "1001", Carrier = "Yurtiçi Kargo", TrackingNumber = "YK123" };

        var n = CustomerNotifications.ForOrder("1044", order, OrderStatusEmailService.Shipped)!;

        n.Kind.Should().Be(CustomerNotificationKinds.OrderShipped);
        n.ReferenceId.Should().Be("1044");
        n.Text.Should().Contain("#1044").And.Contain("Yurtiçi Kargo").And.Contain("YK123");
    }

    [Fact]
    public void Shipped_WithoutCarrierInfo_StillReads()
    {
        var n = CustomerNotifications.ForOrder("1044", new OrderInfo { CustomerId = "1001" }, OrderStatusEmailService.Shipped)!;

        n.Text.Should().Contain("#1044").And.NotContain("()").And.NotContain("Takip no:");
    }

    [Fact]
    public void Delivered_SaysDelivered()
    {
        var n = CustomerNotifications.ForOrder("1044", new OrderInfo { CustomerId = "1001" }, OrderStatusEmailService.Delivered)!;

        n.Kind.Should().Be(CustomerNotificationKinds.OrderDelivered);
        n.Title.Should().Contain("teslim");
    }

    [Fact]
    public void UnknownOrderKind_ProducesNothing() =>
        CustomerNotifications.ForOrder("1044", new OrderInfo(), "weird").Should().BeNull();

    [Theory]
    [InlineData(ComplaintStatusEmailService.InReview, CustomerNotificationKinds.ComplaintInReview)]
    [InlineData(ComplaintStatusEmailService.Closed, CustomerNotificationKinds.ComplaintClosed)]
    public void Complaint_MapsTheStatusKind(string kind, string expected)
    {
        var n = CustomerNotifications.ForComplaint("c-9", new ComplaintInfo { OrderId = "1030", CustomerId = "1001" }, kind)!;

        n.Kind.Should().Be(expected);
        n.ReferenceId.Should().Be("c-9");
        n.Text.Should().Contain("#1030");
    }

    [Fact]
    public void UnknownComplaintKind_ProducesNothing() =>
        CustomerNotifications.ForComplaint("c-9", new ComplaintInfo(), "weird").Should().BeNull();
}

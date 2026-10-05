// Sipariş durum güncellemesi + proaktif e-posta: yalnızca gerçek geçişte, çok pod'da bir kez.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Notifications;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Notifications;
using CustomerSupportBot.Application.Services.Orders;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class OrderFulfillmentServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static OrderInfo Order(string status) => new()
    {
        CustomerId = "1001", Status = status, Carrier = "Yurtiçi Kargo", TrackingNumber = "YK<123>",
        Lines = [new OrderLine("Kahve Makinesi", 1)]
    };

    private sealed record Harness(
        OrderFulfillmentService Service, IOrderRepository Orders, ICustomerRepository Customers,
        IEmailSender Email, List<EmailMessage> Sent, InMemoryNotificationLedger Ledger);

    private static Harness Build(
        OrderStatusChange change = OrderStatusChange.Updated, string status = "Kargolandı",
        Action<EmailOptions>? configure = null, InMemoryNotificationLedger? ledger = null)
    {
        var orders = Substitute.For<IOrderRepository>();
        orders.MarkShipped(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<DateTime>())
            .Returns(new OrderStatusUpdateResult(change, Order(status)));
        orders.MarkDelivered(Arg.Any<string>(), Arg.Any<DateTime>())
            .Returns(new OrderStatusUpdateResult(change, Order(WellKnown.OrderStatuses.Delivered)));
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetEmailAsync(1001, Arg.Any<CancellationToken>()).Returns("ayse@example.com");
        customers.GetFullNameAsync(1001, Arg.Any<CancellationToken>()).Returns("Ayşe Yılmaz");
        var sent = new List<EmailMessage>();
        var email = Substitute.For<IEmailSender>();
        email.IsEnabled.Returns(true);
        email.SendAsync(Arg.Do<EmailMessage>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var options = new EmailOptions { Enabled = true, FromAddress = "destek@example.com", PublicBaseUrl = "https://destek.example.com" };
        configure?.Invoke(options);
        ledger ??= new InMemoryNotificationLedger();
        var notifier = new OrderStatusEmailService(email, ledger, customers, Options.Create(options),
            NullLogger<OrderStatusEmailService>.Instance);
        return new Harness(new OrderFulfillmentService(orders, notifier), orders, customers, email, sent, ledger);
    }

    [Fact]
    public async Task Shipped_UpdatesTheOrder_AndEmailsTheCustomerWithTracking()
    {
        var h = Build();

        var r = await h.Service.MarkShippedAsync("1044", " Yurtiçi Kargo ", " YK<123> ", Ct);

        r.Status.Should().Be(OrderFulfillmentStatus.Updated);
        h.Orders.Received(1).MarkShipped("1044", "Yurtiçi Kargo", "YK<123>", Arg.Any<DateTime>());
        var mail = h.Sent.Should().ContainSingle().Subject;
        mail.To.Should().Be("ayse@example.com");
        mail.Subject.Should().Be("Siparişiniz kargoya verildi — #1044");
        mail.TextBody.Should().Contain("Merhaba Ayşe Yılmaz").And.Contain("Kahve Makinesi")
            .And.Contain("Yurtiçi Kargo").And.Contain("YK<123>").And.Contain("https://destek.example.com/");
        mail.HtmlBody.Should().Contain("YK&lt;123&gt;").And.NotContain("YK<123>");
    }

    [Fact]
    public async Task Delivered_EmailsTheCustomer()
    {
        var h = Build();

        (await h.Service.MarkDeliveredAsync("1044", Ct)).Status.Should().Be(OrderFulfillmentStatus.Updated);

        h.Sent.Should().ContainSingle().Which.Subject.Should().Be("Siparişiniz teslim edildi — #1044");
    }

    [Theory]
    [InlineData(OrderStatusChange.Unchanged, OrderFulfillmentStatus.Unchanged)]
    [InlineData(OrderStatusChange.InvalidTransition, OrderFulfillmentStatus.InvalidTransition)]
    [InlineData(OrderStatusChange.NotFound, OrderFulfillmentStatus.NotFound)]
    public async Task NoTransition_NoEmail(OrderStatusChange change, OrderFulfillmentStatus expected)
    {
        var h = Build(change);

        (await h.Service.MarkShippedAsync("1044", null, null, Ct)).Status.Should().Be(expected);

        h.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task TooLongCarrierOrTracking_IsInvalid_AndTheOrderIsNotTouched()
    {
        var h = Build();

        (await h.Service.MarkShippedAsync("1044", new string('k', 65), null, Ct)).Status.Should().Be(OrderFulfillmentStatus.Invalid);
        (await h.Service.MarkShippedAsync("1044", null, new string('t', 65), Ct)).Status.Should().Be(OrderFulfillmentStatus.Invalid);

        h.Orders.DidNotReceiveWithAnyArgs().MarkShipped(default!, default, default, default);
    }

    [Fact]
    public async Task NotificationTurnedOff_NoEmail()
    {
        var h = Build(configure: o => o.Notifications.OrderShipped = false);

        await h.Service.MarkShippedAsync("1044", null, null, Ct);

        h.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task TwoPods_TheSameTransition_EmailsOnce()
    {
        var ledger = new InMemoryNotificationLedger();
        var (a, b) = (Build(ledger: ledger), Build(ledger: ledger));

        await a.Service.MarkShippedAsync("1044", null, null, Ct);
        await b.Service.MarkShippedAsync("1044", null, null, Ct);

        (a.Sent.Count + b.Sent.Count).Should().Be(1);
    }

    [Fact]
    public async Task SendFailure_ReleasesTheClaim_ButTheOrderIsStillUpdated()
    {
        var h = Build();
        h.Email.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("smtp")));

        (await h.Service.MarkShippedAsync("1044", null, null, Ct)).Status.Should().Be(OrderFulfillmentStatus.Updated);

        (await h.Ledger.TryClaimAsync("order-status:1044:shipped", Ct)).Should().BeTrue();
    }

    [Fact]
    public async Task CustomerWithoutEmail_IsSkipped()
    {
        var h = Build();
        h.Customers.GetEmailAsync(1001, Arg.Any<CancellationToken>()).Returns((string?)null);

        await h.Service.MarkShippedAsync("1044", null, null, Ct);

        h.Sent.Should().BeEmpty();
    }
}

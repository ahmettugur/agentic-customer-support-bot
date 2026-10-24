// Proaktif ajan: sistem müşteri sormadan geciken kargoyu fark edip bildirir (e-posta kalıcı, sohbet içi anlık).

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Notifications;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Notifications;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class ProactiveOutreachServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed record Harness(
        ProactiveOutreachService Service, IOrderRepository Orders, List<EmailMessage> Sent,
        List<CustomerNotification> Pushed, InMemoryNotificationLedger Ledger, IEmailSender Email);

    private static OrderInfo Shipped(string customer = "1001", int daysAgo = 8) => new()
    {
        CustomerId = customer, Status = WellKnown.OrderStatuses.Shipped, Lines = [new OrderLine("Kahve <Makinesi>", 1)],
        Carrier = "Yurtiçi Kargo", TrackingNumber = "YK1", ShippedAt = Now.UtcDateTime.AddDays(-daysAgo)
    };

    private static Harness Build(
        IReadOnlyList<(string, OrderInfo)>? delayed = null, Action<ProactiveOutreachOptions>? configure = null,
        bool emailEnabled = true, string? email = "ayse@example.com")
    {
        var orders = Substitute.For<IOrderRepository>();
        orders.GetShippedBefore(Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<DateTime?>()).Returns(delayed ?? [("1044", Shipped())]);
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetEmailAsync(1001, Arg.Any<CancellationToken>()).Returns(email);
        customers.GetFullNameAsync(1001, Arg.Any<CancellationToken>()).Returns("Ayşe Yılmaz");
        var sent = new List<EmailMessage>();
        var sender = Substitute.For<IEmailSender>();
        sender.IsEnabled.Returns(emailEnabled);
        sender.SendAsync(Arg.Do<EmailMessage>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var ledger = new InMemoryNotificationLedger();
        var hub = new CustomerNotificationHub(null, NullLogger<CustomerNotificationHub>.Instance);
        var pushed = new List<CustomerNotification>();
        hub.Subscribe("1001", n => { pushed.Add(n); return Task.CompletedTask; });
        var o = new ProactiveOutreachOptions { Enabled = true };
        configure?.Invoke(o);
        var svc = new ProactiveOutreachService(orders, sender, ledger, customers, hub, Options.Create(o),
            Options.Create(new EmailOptions { Enabled = emailEnabled, FromAddress = "destek@example.com", PublicBaseUrl = "https://destek.example.com" }),
            NullLogger<ProactiveOutreachService>.Instance, new FixedClock(Now));
        return new Harness(svc, orders, sent, pushed, ledger, sender);
    }

    [Fact]
    public async Task ADelayedShipment_EmailsAndNotifiesInChat()
    {
        var h = Build();

        var n = await h.Service.RunOnceAsync(Ct);

        n.Should().Be(1);
        var mail = h.Sent.Should().ContainSingle().Subject;
        mail.To.Should().Be("ayse@example.com");
        mail.Subject.Should().Contain("#1044");
        mail.TextBody.Should().Contain("8 gün").And.Contain("YK1");
        mail.HtmlBody.Should().Contain("Kahve &lt;Makinesi&gt;").And.NotContain("<Makinesi>");
        h.Pushed.Should().ContainSingle().Which.Kind.Should().Be(CustomerNotificationKinds.ShipmentDelayed);
    }

    [Fact]
    public async Task TheSameOrder_IsNeverNotifiedTwice()
    {
        var h = Build();

        await h.Service.RunOnceAsync(Ct);
        var second = await h.Service.RunOnceAsync(Ct);

        second.Should().Be(0);
        h.Sent.Should().HaveCount(1);
        h.Pushed.Should().HaveCount(1);
    }

    [Fact]
    public async Task TheCutoff_IsNowMinusTheConfiguredDays_AndTheBatchSizeIsPassedAsTheLimit()
    {
        var h = Build(configure: o => { o.DelayedShipmentDays = 3; o.BatchSize = 7; });

        await h.Service.RunOnceAsync(Ct);

        h.Orders.Received(1).GetShippedBefore(Now.UtcDateTime.AddDays(-3), 7, Now.UtcDateTime.AddDays(-30));
    }

    [Fact]
    public async Task TheScanWindow_HasALowerBound_SoHistoricalOrdersAreNeverMailed()
    {
        var h = Build(configure: o => o.MaxAgeDays = 10);

        await h.Service.RunOnceAsync(Ct);

        h.Orders.Received(1).GetShippedBefore(Arg.Any<DateTime>(), Arg.Any<int>(), Now.UtcDateTime.AddDays(-10));
    }

    [Fact]
    public async Task WhenDisabled_NothingIsQueriedOrSent()
    {
        var h = Build(configure: o => o.Enabled = false);

        (await h.Service.RunOnceAsync(Ct)).Should().Be(0);

        h.Orders.DidNotReceive().GetShippedBefore(Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<DateTime?>());
        h.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task WithEmailDisabled_TheChatNotificationStillGoes_AndNoMailIsSent()
    {
        var h = Build(emailEnabled: false);

        await h.Service.RunOnceAsync(Ct);

        h.Sent.Should().BeEmpty();
        h.Pushed.Should().ContainSingle();
    }

    [Fact]
    public async Task ACustomerWithoutAnEmail_StillGetsTheChatNotification_AndTheClaimIsKept()
    {
        var h = Build(email: null);

        await h.Service.RunOnceAsync(Ct);
        await h.Service.RunOnceAsync(Ct);

        h.Sent.Should().BeEmpty();
        h.Pushed.Should().ContainSingle("adres yokken yeniden denemenin anlamı yok");
    }

    [Fact]
    public async Task AFailedSend_ReleasesTheClaim_SoTheNextRunRetries()
    {
        var h = Build();
        var calls = 0;
        h.Email.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++calls == 1 ? Task.FromException(new InvalidOperationException("smtp")) : Task.CompletedTask);

        await h.Service.RunOnceAsync(Ct);
        await h.Service.RunOnceAsync(Ct);

        calls.Should().Be(2);
    }

    [Fact]
    public async Task OneBrokenOrder_DoesNotStopTheOthers()
    {
        var delayed = new List<(string, OrderInfo)> { ("1", Shipped("not-a-number")), ("1044", Shipped()) };
        var h = Build(delayed);

        var n = await h.Service.RunOnceAsync(Ct);

        n.Should().Be(1);
        h.Sent.Should().ContainSingle();
    }
}

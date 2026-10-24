// Proaktif ajan, şikayet tarafı: uzun süredir sonuçlanmayan şikayet için müşteriye sormadan haber verilir (bir kez).

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Notifications;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Notifications;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class ProactiveStaleComplaintTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static ComplaintListItem Item(string id, string status = "İncelemede", int updatedDaysAgo = 5, string customer = "1001") => new(id, new ComplaintInfo
    {
        CustomerId = customer, OrderId = "1030", Status = status, Complaint = "GİZLİ METİN", UpdatedAt = Now.UtcDateTime.AddDays(-updatedDaysAgo)
    });

    private sealed record Rig(ProactiveOutreachService Svc, List<EmailMessage> Sent, List<CustomerNotification> Pushed, IComplaintRepository Complaints);

    private static Rig Build(IReadOnlyList<ComplaintListItem> open, Action<ProactiveOutreachOptions>? configure = null)
    {
        var orders = Substitute.For<IOrderRepository>();
        orders.GetShippedBefore(Arg.Any<DateTime>(), Arg.Any<int>()).Returns([]);
        var complaints = Substitute.For<IComplaintRepository>();
        complaints.ListAsync(Arg.Any<ComplaintQuery>(), Arg.Any<CancellationToken>()).Returns(new ComplaintPage(open, open.Count));
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetEmailAsync(1001, Arg.Any<CancellationToken>()).Returns("ayse@example.com");
        customers.GetFullNameAsync(1001, Arg.Any<CancellationToken>()).Returns("Ayşe");
        var sent = new List<EmailMessage>();
        var sender = Substitute.For<IEmailSender>();
        sender.IsEnabled.Returns(true);
        sender.SendAsync(Arg.Do<EmailMessage>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var hub = new CustomerNotificationHub(null, NullLogger<CustomerNotificationHub>.Instance);
        var pushed = new List<CustomerNotification>();
        hub.Subscribe("1001", n => { pushed.Add(n); return Task.CompletedTask; });
        var o = new ProactiveOutreachOptions { Enabled = true };
        configure?.Invoke(o);
        var svc = new ProactiveOutreachService(orders, sender, new InMemoryNotificationLedger(), customers, hub, Options.Create(o),
            Options.Create(new EmailOptions { Enabled = true, FromAddress = "destek@example.com" }),
            NullLogger<ProactiveOutreachService>.Instance, new FakeTimeProvider(Now), complaints);
        return new Rig(svc, sent, pushed, complaints);
    }

    [Fact]
    public async Task AStaleOpenComplaint_EmailsAndNotifiesInChat_WithoutItsFreeText()
    {
        var r = Build([Item("c-1")]);

        (await r.Svc.RunOnceAsync(Ct)).Should().Be(1);

        var mail = r.Sent.Should().ContainSingle().Subject;
        mail.Subject.Should().Contain("c-1");
        (mail.TextBody + mail.HtmlBody).Should().Contain("5 gün").And.NotContain("GİZLİ METİN");
        r.Pushed.Should().ContainSingle().Which.Kind.Should().Be(CustomerNotificationKinds.ComplaintWaiting);
    }

    [Fact]
    public async Task RecentlyUpdatedComplaints_AreLeftAlone()
    {
        var r = Build([Item("c-1", updatedDaysAgo: 1)]);

        (await r.Svc.RunOnceAsync(Ct)).Should().Be(0);
        r.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task EachComplaint_IsRemindedOnce()
    {
        var r = Build([Item("c-1")]);

        await r.Svc.RunOnceAsync(Ct);
        (await r.Svc.RunOnceAsync(Ct)).Should().Be(0);

        r.Sent.Should().HaveCount(1);
    }

    [Fact]
    public async Task TheThreshold_IsConfigurable()
    {
        var r = Build([Item("c-1", updatedDaysAgo: 2)], o => o.StaleComplaintDays = 2);

        (await r.Svc.RunOnceAsync(Ct)).Should().Be(1);
    }

    [Fact]
    public async Task OnlyOpenComplaintsAreQueried_WithinTheBatch()
    {
        var r = Build([Item("c-1")], o => o.BatchSize = 7);

        await r.Svc.RunOnceAsync(Ct);

        await r.Complaints.Received(1).ListAsync(
            Arg.Is<ComplaintQuery>(q => q.Status == ComplaintQuery.OpenStatus && q.PageSize == 7), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ABrokenComplaint_DoesNotStopTheOthers()
    {
        var r = Build([Item("c-bad", customer: "not-a-number"), Item("c-1")]);

        (await r.Svc.RunOnceAsync(Ct)).Should().Be(1);
    }

    [Fact]
    public async Task WithoutAComplaintRepository_OnlyShipmentsAreScanned()
    {
        var orders = Substitute.For<IOrderRepository>();
        orders.GetShippedBefore(Arg.Any<DateTime>(), Arg.Any<int>()).Returns([]);
        var svc = new ProactiveOutreachService(orders, Substitute.For<IEmailSender>(), new InMemoryNotificationLedger(),
            Substitute.For<ICustomerRepository>(), null, Options.Create(new ProactiveOutreachOptions { Enabled = true }),
            Options.Create(new EmailOptions()), NullLogger<ProactiveOutreachService>.Instance, new FakeTimeProvider(Now));

        (await svc.RunOnceAsync(Ct)).Should().Be(0);
    }
}

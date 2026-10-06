// Onay sonucu e-postası: çok pod'da bir kez, yalnızca sonuçlanmış kayıtlar, ayarlardan açılıp kapanır.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Notifications;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Notifications;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class ApprovalResultEmailServiceTests
{
    private sealed class FakeSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];
        public bool Fail { get; set; }
        public bool IsEnabled => true;

        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            if (Fail) throw new InvalidOperationException("SMTP kapalı");
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private static readonly EmailOptions EnabledOptions = new()
    {
        Enabled = true, FromAddress = "destek@example.com", PublicBaseUrl = "https://destek.example.com",
        Smtp = new EmailOptions.SmtpOptions { Host = "smtp.example.com" }
    };

    private sealed record Harness(FakeSender Sender, InMemoryNotificationLedger Ledger, ICustomerRepository Customers,
        Func<ApprovalResultEmailService> NewPod);

    private static Harness Build(EmailOptions? options = null, string? email = "ayse@example.com")
    {
        var sender = new FakeSender();
        var ledger = new InMemoryNotificationLedger();
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetEmailAsync(1001, Arg.Any<CancellationToken>()).Returns(email);
        customers.GetFullNameAsync(1001, Arg.Any<CancellationToken>()).Returns("Ayşe Yılmaz");
        var opts = Options.Create(options ?? EnabledOptions);
        return new Harness(sender, ledger, customers,
            () => new ApprovalResultEmailService(sender, ledger, customers, opts, NullLogger<ApprovalResultEmailService>.Instance));
    }

    private static ApprovalRequest Decided(ApprovalStatus status = ApprovalStatus.Approved,
        ApprovalExecutionStatus exec = ApprovalExecutionStatus.Succeeded, string? customerId = "1001") => new()
    {
        Id = "appr-1", ToolName = WellKnown.ToolNames.ReturnRequest, CustomerId = customerId, Status = status,
        ExecutionStatus = exec, ExecutionResult = "10248 numaralı siparişiniz için iade kaydı oluşturuldu.",
        DecisionReason = status == ApprovalStatus.Rejected ? "İade süresi dolmuş <b>" : null
    };

    [Fact]
    public async Task EveryPodHandlesTheEvent_ButTheEmailGoesOutOnce()
    {
        var h = Build();
        var request = Decided();

        await Task.WhenAll(h.NewPod().HandleDecidedAsync(request, TestContext.Current.CancellationToken), h.NewPod().HandleDecidedAsync(request, TestContext.Current.CancellationToken));

        h.Sender.Sent.Should().ContainSingle();
        var mail = h.Sender.Sent[0];
        mail.To.Should().Be("ayse@example.com");
        mail.ToName.Should().Be("Ayşe Yılmaz");
        mail.Subject.Should().Contain("onaylandı").And.Contain("İade");
        mail.TextBody.Should().Contain("10248 numaralı siparişiniz için iade kaydı oluşturuldu.")
            .And.Contain("https://destek.example.com");
    }

    [Theory]
    [InlineData(ApprovalStatus.Rejected, ApprovalExecutionStatus.None, "reddedildi")]
    [InlineData(ApprovalStatus.Expired, ApprovalExecutionStatus.None, "zaman aşımına uğradı")]
    [InlineData(ApprovalStatus.Approved, ApprovalExecutionStatus.Failed, "tamamlanamadı")]
    public async Task SubjectReflectsTheOutcome(ApprovalStatus status, ApprovalExecutionStatus exec, string expected)
    {
        var h = Build();

        await h.NewPod().HandleDecidedAsync(Decided(status, exec), TestContext.Current.CancellationToken);

        h.Sender.Sent.Should().ContainSingle().Which.Subject.Should().Contain(expected);
    }

    [Fact]
    public async Task UserTextIsHtmlEscaped()
    {
        var h = Build();

        await h.NewPod().HandleDecidedAsync(Decided(ApprovalStatus.Rejected, ApprovalExecutionStatus.None), TestContext.Current.CancellationToken);

        var mail = h.Sender.Sent.Single();
        mail.HtmlBody.Should().Contain("&lt;b&gt;").And.NotContain("dolmuş <b>");
        mail.TextBody.Should().Contain("İade süresi dolmuş <b>");
    }

    [Theory]
    [InlineData(ApprovalStatus.Pending, ApprovalExecutionStatus.None)]
    [InlineData(ApprovalStatus.Approved, ApprovalExecutionStatus.Running)]
    public async Task NotYetFinal_IsSkipped(ApprovalStatus status, ApprovalExecutionStatus exec)
    {
        var h = Build();

        await h.NewPod().HandleDecidedAsync(Decided(status, exec), TestContext.Current.CancellationToken);

        h.Sender.Sent.Should().BeEmpty();
        (await h.Ledger.TryClaimAsync("approval-result:appr-1", TestContext.Current.CancellationToken)).Should().BeTrue("atlanan kayıt talep edilmemeli");
    }

    [Fact]
    public async Task Disabled_OrApprovalNotificationsOff_SendsNothing()
    {
        var off = Build(new EmailOptions { Enabled = false });
        var notificationsOff = Build(new EmailOptions
        {
            Enabled = true, FromAddress = "destek@example.com", Smtp = new EmailOptions.SmtpOptions { Host = "h" },
            Notifications = new EmailOptions.NotificationOptions { ApprovalResults = false }
        });

        await off.NewPod().HandleDecidedAsync(Decided(), TestContext.Current.CancellationToken);
        await notificationsOff.NewPod().HandleDecidedAsync(Decided(), TestContext.Current.CancellationToken);

        off.Sender.Sent.Should().BeEmpty();
        notificationsOff.Sender.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task NoCustomer_OrNoEmailOnFile_SendsNothing()
    {
        var h = Build(email: null);

        await h.NewPod().HandleDecidedAsync(Decided(customerId: null), TestContext.Current.CancellationToken);
        await h.NewPod().HandleDecidedAsync(Decided(), TestContext.Current.CancellationToken);

        h.Sender.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task SendFailure_ReleasesTheClaim_SoItCanBeRetried()
    {
        var h = Build();
        h.Sender.Fail = true;

        await h.NewPod().HandleDecidedAsync(Decided(), TestContext.Current.CancellationToken);

        h.Sender.Sent.Should().BeEmpty();
        h.Sender.Fail = false;
        await h.NewPod().HandleDecidedAsync(Decided(), TestContext.Current.CancellationToken);
        h.Sender.Sent.Should().ContainSingle();
    }
}

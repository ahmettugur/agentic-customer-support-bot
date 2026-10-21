// Şikayet yönetimi: kural → depo → bildirim. Depo sahte; e-posta defteri gerçek (InMemory).

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Notifications;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Complaints;
using CustomerSupportBot.Application.Services.Notifications;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class ComplaintManagementServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly ComplaintActor Admin = new(ComplaintActorRole.Admin, null, "admin");
    private static readonly ComplaintActor AgentA = new(ComplaintActorRole.Agent, "a1", "ayse");

    /// <summary>Sahte depo geçişleri <see cref="Current"/>'ı günceller — ardışık komutlar gerçek bir kayıt gibi ilerler.</summary>
    private sealed class Harness
    {
        public ComplaintManagementService Service { get; set; } = null!;
        public IComplaintRepository Repo { get; } = Substitute.For<IComplaintRepository>();
        public List<EmailMessage> Sent { get; } = [];
        public IEmailSender Email { get; } = Substitute.For<IEmailSender>();
        public ComplaintInfo Current { get; set; } = null!;
        public List<CustomerNotification> Pushed { get; } = [];
    }

    private static Harness Build(string status = WellKnown.ComplaintStatuses.Pending, string? assigned = null,
        bool agentActive = true, string? approvalId = null)
    {
        var h = new Harness
        {
            Current = new ComplaintInfo
            {
                OrderId = "1030", CustomerId = "1001", Complaint = "Kutu <ezik> geldi", Status = status,
                AssignedAgentId = assigned, Version = 3, ApprovalId = approvalId,
                CreatedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        };
        var repo = h.Repo;
        repo.Get("1006").Returns(_ => h.Current);
        repo.TryTransitionAsync(Arg.Any<ComplaintTransition>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            var t = ci.Arg<ComplaintTransition>();
            h.Current = new ComplaintInfo
            {
                OrderId = h.Current.OrderId, CustomerId = h.Current.CustomerId, Complaint = h.Current.Complaint,
                Status = t.NewStatus, AssignedAgentId = t.AssignedAgentId, Resolution = t.Resolution ?? h.Current.Resolution,
                Version = h.Current.Version + 1, CreatedAt = h.Current.CreatedAt, ApprovalId = h.Current.ApprovalId
            };
            return new ComplaintTransitionOutcome(ComplaintTransitionChange.Updated, h.Current);
        });
        repo.AddEventAsync(Arg.Any<string>(), Arg.Any<ComplaintEvent>(), Arg.Any<CancellationToken>()).Returns(true);

        var agents = Substitute.For<IHumanAgentRegistry>();
        agents.Get("a1").Returns(new HumanAgent { Id = "a1", DisplayName = "Ayşe", IsActive = agentActive });
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetEmailAsync(1001, Arg.Any<CancellationToken>()).Returns("musteri@example.com");
        customers.GetFullNameAsync(1001, Arg.Any<CancellationToken>()).Returns("Can Demir");
        h.Email.IsEnabled.Returns(true);
        h.Email.SendAsync(Arg.Do<EmailMessage>(h.Sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var notifier = new ComplaintStatusEmailService(h.Email, new InMemoryNotificationLedger(), customers,
            Options.Create(new EmailOptions { Enabled = true, FromAddress = "destek@example.com" }),
            NullLogger<ComplaintStatusEmailService>.Instance);

        var approvals = Substitute.For<IApprovalQueue>();
        approvals.GetAsync("apr-1", Arg.Any<CancellationToken>()).Returns(new ApprovalRequest
        {
            Id = "apr-1",
            Parameters = new Dictionary<string, object?> { ["attachmentIds"] = new List<string> { "att-1", "att-2" } }
        });
        var orders = Substitute.For<IOrderRepository>();
        orders.Get("1030").Returns(new OrderInfo { CustomerId = "1001", Status = "Teslim Edildi", Lines = [new OrderLine("Kahve", 1)] });

        var hub = new CustomerNotificationHub(null, NullLogger<CustomerNotificationHub>.Instance);
        hub.Subscribe("1001", n => { h.Pushed.Add(n); return Task.CompletedTask; });
        h.Service = new ComplaintManagementService(repo, orders, approvals, agents, notifier,
            NullLogger<ComplaintManagementService>.Instance, hub: hub);
        return h;
    }

    [Fact]
    public async Task Claim_MovesToInProgress_AssignsActor_AndEmailsInReview()
    {
        var h = Build();

        var r = await h.Service.ExecuteAsync("1006", AgentA, new ComplaintCommand(ComplaintAction.Claim), Ct);

        r.Status.Should().Be(ComplaintCommandStatus.Updated);
        await h.Repo.Received(1).TryTransitionAsync(Arg.Is<ComplaintTransition>(t =>
            t.ExpectedStatus == WellKnown.ComplaintStatuses.Pending && t.ExpectedVersion == 3
            && t.NewStatus == WellKnown.ComplaintStatuses.InProgress && t.AssignedAgentId == "a1"
            && t.Event.Kind == ComplaintEventKinds.Claimed && t.Event.Actor == "ayse"), Arg.Any<CancellationToken>());
        var mail = h.Sent.Should().ContainSingle().Subject;
        mail.Subject.Should().Be("Şikayetiniz inceleniyor — #1006");
        mail.TextBody.Should().Contain("Can Demir").And.Contain("1030");
    }

    [Fact]
    public async Task Resolve_SendsClosedEmail_WithEscapedResolution_AndInternalNoteGoesToEventOnly()
    {
        var h = Build(WellKnown.ComplaintStatuses.InProgress, "a1");

        var r = await h.Service.ExecuteAsync("1006", AgentA,
            new ComplaintCommand(ComplaintAction.Resolve, Text: " Yeni <ürün> gönderildi. ", InternalNote: "depo onayladı"), Ct);

        r.Status.Should().Be(ComplaintCommandStatus.Updated);
        await h.Repo.Received(1).TryTransitionAsync(Arg.Is<ComplaintTransition>(t =>
            t.Resolution == "Yeni <ürün> gönderildi." && t.Event.Note == "depo onayladı"), Arg.Any<CancellationToken>());
        var mail = h.Sent.Should().ContainSingle().Subject;
        mail.Subject.Should().Be("Şikayetiniz çözüldü — #1006");
        mail.TextBody.Should().Contain("Yeni <ürün> gönderildi.").And.NotContain("depo onayladı");
        // WebUtility.HtmlEncode Türkçe harfleri de sayısal varlığa çevirir (sipariş e-postalarıyla aynı).
        mail.HtmlBody.Should().Contain("Yeni &lt;").And.Contain("&gt; g").And.NotContain("<ürün>").And.NotContain("depo onayladı");
    }

    [Fact]
    public async Task Reject_UsesRejectedSubject()
    {
        var h = Build(WellKnown.ComplaintStatuses.InProgress, "a1");

        await h.Service.ExecuteAsync("1006", Admin, new ComplaintCommand(ComplaintAction.Reject, Text: "Hasar kargodan."), Ct);

        h.Sent.Should().ContainSingle().Which.Subject.Should().Be("Şikayetiniz sonuçlandı — #1006");
    }

    [Fact]
    public async Task ReopenThenCloseAgain_SendsSecondClosedEmail()
    {
        var h = Build(WellKnown.ComplaintStatuses.InProgress, "a1");

        await h.Service.ExecuteAsync("1006", AgentA, new ComplaintCommand(ComplaintAction.Resolve, Text: "çözüldü"), Ct);
        await h.Service.ExecuteAsync("1006", Admin, new ComplaintCommand(ComplaintAction.Reopen, Text: "müşteri itiraz etti"), Ct);
        await h.Service.ExecuteAsync("1006", AgentA, new ComplaintCommand(ComplaintAction.Resolve, Text: "ikinci çözüm"), Ct);

        h.Sent.Select(m => m.Subject).Should().Equal(
            "Şikayetiniz çözüldü — #1006", "Şikayetiniz inceleniyor — #1006", "Şikayetiniz çözüldü — #1006");
    }

    [Fact]
    public async Task Reassign_InProgress_DoesNotEmail()
    {
        var h = Build(WellKnown.ComplaintStatuses.InProgress, "b2");

        (await h.Service.ExecuteAsync("1006", Admin, new ComplaintCommand(ComplaintAction.Assign, AssigneeAgentId: "a1"), Ct))
            .Status.Should().Be(ComplaintCommandStatus.Updated);

        h.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Assign_InactiveAgent_Invalid()
    {
        var h = Build(agentActive: false);

        var r = await h.Service.ExecuteAsync("1006", Admin, new ComplaintCommand(ComplaintAction.Assign, AssigneeAgentId: "a1"), Ct);

        r.Status.Should().Be(ComplaintCommandStatus.Invalid);
        await h.Repo.DidNotReceive().TryTransitionAsync(Arg.Any<ComplaintTransition>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(WellKnown.ComplaintStatuses.Pending, null, ComplaintCommandStatus.InvalidTransition)]
    [InlineData(WellKnown.ComplaintStatuses.InProgress, "b2", ComplaintCommandStatus.Forbidden)]
    public async Task Rules_MapToStatus(string status, string? assigned, ComplaintCommandStatus expected)
    {
        var h = Build(status, assigned);

        (await h.Service.ExecuteAsync("1006", AgentA, new ComplaintCommand(ComplaintAction.Resolve, Text: "x"), Ct))
            .Status.Should().Be(expected);
    }

    [Fact]
    public async Task MissingText_Invalid_WithMessage()
    {
        var h = Build(WellKnown.ComplaintStatuses.InProgress, "a1");

        var r = await h.Service.ExecuteAsync("1006", AgentA, new ComplaintCommand(ComplaintAction.Resolve, Text: " "), Ct);

        r.Status.Should().Be(ComplaintCommandStatus.Invalid);
        r.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task StaleExpectedVersion_Conflict_WithoutWriting()
    {
        var h = Build();

        var r = await h.Service.ExecuteAsync("1006", AgentA, new ComplaintCommand(ComplaintAction.Claim, ExpectedVersion: 2), Ct);

        r.Status.Should().Be(ComplaintCommandStatus.Conflict);
        r.Complaint!.Version.Should().Be(3);
        await h.Repo.DidNotReceive().TryTransitionAsync(Arg.Any<ComplaintTransition>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RepoConflict_IsConflict_AndNoEmail()
    {
        var h = Build();
        h.Repo.TryTransitionAsync(Arg.Any<ComplaintTransition>(), Arg.Any<CancellationToken>())
            .Returns(new ComplaintTransitionOutcome(ComplaintTransitionChange.Conflict, h.Current));

        (await h.Service.ExecuteAsync("1006", AgentA, new ComplaintCommand(ComplaintAction.Claim), Ct))
            .Status.Should().Be(ComplaintCommandStatus.Conflict);
        h.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task EmailFailure_DoesNotUndoTheTransition()
    {
        var h = Build();
        h.Email.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("smtp")));

        (await h.Service.ExecuteAsync("1006", AgentA, new ComplaintCommand(ComplaintAction.Claim), Ct))
            .Status.Should().Be(ComplaintCommandStatus.Updated);
    }

    [Fact]
    public async Task Note_WritesEventOnly()
    {
        var h = Build(WellKnown.ComplaintStatuses.InProgress, "a1");

        (await h.Service.ExecuteAsync("1006", AgentA, new ComplaintCommand(ComplaintAction.Note, Text: "kargoya soruldu"), Ct))
            .Status.Should().Be(ComplaintCommandStatus.Updated);

        await h.Repo.Received(1).AddEventAsync("1006",
            Arg.Is<ComplaintEvent>(e => e.Kind == ComplaintEventKinds.Note && e.Note == "kargoya soruldu"), Arg.Any<CancellationToken>());
        await h.Repo.DidNotReceive().TryTransitionAsync(Arg.Any<ComplaintTransition>(), Arg.Any<CancellationToken>());
        h.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Unknown_NotFound()
        => (await Build().Service.ExecuteAsync("404", AgentA, new ComplaintCommand(ComplaintAction.Claim), Ct))
            .Status.Should().Be(ComplaintCommandStatus.NotFound);

    [Fact]
    public async Task Detail_IncludesOrder_PhotosFromApproval_AndEvents()
    {
        var h = Build(approvalId: "apr-1");
        h.Repo.GetEventsAsync("1006", Arg.Any<CancellationToken>()).Returns(
            [new ComplaintEvent(DateTime.UtcNow, "system", ComplaintEventKinds.Created, null, "Beklemede", null, null)]);

        var d = await h.Service.GetAsync("1006", Ct);

        d!.Order!.LinesSummary().Should().Be("Kahve x1");
        d.AttachmentIds.Should().Equal("att-1", "att-2");
        d.Events.Should().ContainSingle();
    }

    [Fact]
    public async Task Claim_PushesAnInReviewNotification_ToTheComplainant()
    {
        var h = Build();

        await h.Service.ExecuteAsync("1006", AgentA, new ComplaintCommand(ComplaintAction.Claim), Ct);

        h.Pushed.Should().ContainSingle().Which.Kind.Should().Be(CustomerNotificationKinds.ComplaintInReview);
    }

    [Fact]
    public async Task Resolve_PushesAClosedNotification()
    {
        var h = Build(WellKnown.ComplaintStatuses.InProgress, assigned: "a1");

        await h.Service.ExecuteAsync("1006", AgentA, new ComplaintCommand(ComplaintAction.Resolve, Text: "çözüldü"), Ct);

        h.Pushed.Should().ContainSingle().Which.Kind.Should().Be(CustomerNotificationKinds.ComplaintClosed);
    }

    [Fact]
    public async Task ARejectedCommand_PushesNothing()
    {
        var h = Build(WellKnown.ComplaintStatuses.InProgress, assigned: "a1");

        await h.Service.ExecuteAsync("1006", AgentA, new ComplaintCommand(ComplaintAction.Resolve, Text: " "), Ct);

        h.Pushed.Should().BeEmpty();
    }
}

// Şikayet geçiş kuralları: durum → yetki → girdi sırasıyla.

using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;

namespace CustomerSupportBot.Domain.Tests;

public class ComplaintTransitionsTests
{
    private static readonly ComplaintActor Admin = new(ComplaintActorRole.Admin, null, "admin");
    private static readonly ComplaintActor AgentA = new(ComplaintActorRole.Agent, "a1", "Ayşe");
    private static readonly ComplaintActor AgentB = new(ComplaintActorRole.Agent, "b2", "Burak");

    private static ComplaintInfo C(string status, string? assigned = null) => new()
    {
        OrderId = "1030", CustomerId = "1001", Complaint = "Ürün kırık geldi", Status = status, AssignedAgentId = assigned
    };

    private const string Pending = WellKnown.ComplaintStatuses.Pending;
    private const string InProgress = WellKnown.ComplaintStatuses.InProgress;
    private const string Resolved = WellKnown.ComplaintStatuses.Resolved;
    private const string Rejected = WellKnown.ComplaintStatuses.Rejected;

    [Fact]
    public void Claim_ByAgent_FromPending_GoesToInProgress()
    {
        var r = ComplaintTransitions.Check(ComplaintAction.Claim, C(Pending), AgentA, null, null);
        r.Allowed.Should().BeTrue();
        r.TargetStatus.Should().Be(InProgress);
    }

    [Fact]
    public void Claim_ByAdmin_Forbidden()
        => ComplaintTransitions.Check(ComplaintAction.Claim, C(Pending), Admin, null, null)
            .Error.Should().Be(ComplaintTransitionErrors.Forbidden);

    [Fact]
    public void Claim_WhenAlreadyInProgress_InvalidTransition()
        => ComplaintTransitions.Check(ComplaintAction.Claim, C(InProgress, "b2"), AgentA, null, null)
            .Error.Should().Be(ComplaintTransitionErrors.InvalidTransition);

    [Theory]
    [InlineData(WellKnown.ComplaintStatuses.Pending)]
    [InlineData(WellKnown.ComplaintStatuses.InProgress)]
    public void Assign_ByAdmin_FromOpen_GoesToInProgress(string status)
    {
        var r = ComplaintTransitions.Check(ComplaintAction.Assign, C(status, "b2"), Admin, "a1", null);
        r.Allowed.Should().BeTrue();
        r.TargetStatus.Should().Be(InProgress);
    }

    [Fact]
    public void Assign_ByAgent_Forbidden()
        => ComplaintTransitions.Check(ComplaintAction.Assign, C(Pending), AgentA, "a1", null)
            .Error.Should().Be(ComplaintTransitionErrors.Forbidden);

    [Fact]
    public void Assign_WithoutAssignee_Required()
        => ComplaintTransitions.Check(ComplaintAction.Assign, C(Pending), Admin, " ", null)
            .Error.Should().Be(ComplaintTransitionErrors.AssigneeRequired);

    [Fact]
    public void Assign_Closed_InvalidTransition()
        => ComplaintTransitions.Check(ComplaintAction.Assign, C(Resolved, "a1"), Admin, "b2", null)
            .Error.Should().Be(ComplaintTransitionErrors.InvalidTransition);

    [Theory]
    [InlineData(ComplaintAction.Resolve, WellKnown.ComplaintStatuses.Resolved)]
    [InlineData(ComplaintAction.Reject, WellKnown.ComplaintStatuses.Rejected)]
    public void Close_ByAssignedAgent_WithText(ComplaintAction action, string target)
    {
        var r = ComplaintTransitions.Check(action, C(InProgress, "a1"), AgentA, null, "Yeni ürün gönderildi.");
        r.Allowed.Should().BeTrue();
        r.TargetStatus.Should().Be(target);
    }

    [Fact]
    public void Resolve_ByAdmin_Allowed()
        => ComplaintTransitions.Check(ComplaintAction.Resolve, C(InProgress, "a1"), Admin, null, "Tamam").Allowed.Should().BeTrue();

    [Fact]
    public void Resolve_ByOtherAgent_Forbidden()
        => ComplaintTransitions.Check(ComplaintAction.Resolve, C(InProgress, "a1"), AgentB, null, "Tamam")
            .Error.Should().Be(ComplaintTransitionErrors.Forbidden);

    [Fact]
    public void Resolve_FromPending_InvalidTransition()
        => ComplaintTransitions.Check(ComplaintAction.Resolve, C(Pending), Admin, null, "Tamam")
            .Error.Should().Be(ComplaintTransitionErrors.InvalidTransition);

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Resolve_WithoutText_TextRequired(string? text)
        => ComplaintTransitions.Check(ComplaintAction.Resolve, C(InProgress, "a1"), AgentA, null, text)
            .Error.Should().Be(ComplaintTransitionErrors.TextRequired);

    [Fact]
    public void Resolve_TooLongText_TooLong()
        => ComplaintTransitions.Check(ComplaintAction.Resolve, C(InProgress, "a1"), AgentA, null, new string('x', 2001))
            .Error.Should().Be(ComplaintTransitionErrors.TooLong);

    [Theory]
    [InlineData(WellKnown.ComplaintStatuses.Resolved)]
    [InlineData(WellKnown.ComplaintStatuses.Rejected)]
    public void Reopen_Closed_ByAssignedAgent_GoesToInProgress(string status)
    {
        var r = ComplaintTransitions.Check(ComplaintAction.Reopen, C(status, "a1"), AgentA, null, "Müşteri tekrar yazdı");
        r.Allowed.Should().BeTrue();
        r.TargetStatus.Should().Be(InProgress);
    }

    [Fact]
    public void Reopen_ByOtherAgent_Forbidden()
        => ComplaintTransitions.Check(ComplaintAction.Reopen, C(Resolved, "a1"), AgentB, null, "neden")
            .Error.Should().Be(ComplaintTransitionErrors.Forbidden);

    [Fact]
    public void Reopen_Open_InvalidTransition()
        => ComplaintTransitions.Check(ComplaintAction.Reopen, C(InProgress, "a1"), Admin, null, "neden")
            .Error.Should().Be(ComplaintTransitionErrors.InvalidTransition);

    [Fact]
    public void Reopen_WithoutReason_TextRequired()
        => ComplaintTransitions.Check(ComplaintAction.Reopen, C(Rejected, "a1"), Admin, null, "")
            .Error.Should().Be(ComplaintTransitionErrors.TextRequired);

    [Fact]
    public void Note_KeepsStatus_ForAssignedAgent()
    {
        var r = ComplaintTransitions.Check(ComplaintAction.Note, C(Resolved, "a1"), AgentA, null, "kargoya sorduk");
        r.Allowed.Should().BeTrue();
        r.TargetStatus.Should().Be(Resolved);
    }

    [Fact]
    public void Note_ByUnassignedAgent_Forbidden()
        => ComplaintTransitions.Check(ComplaintAction.Note, C(Pending), AgentA, null, "not")
            .Error.Should().Be(ComplaintTransitionErrors.Forbidden);

    [Fact]
    public void AvailableActions_ReflectStatusAndRole()
    {
        ComplaintTransitions.AvailableActions(C(Pending), AgentA).Should().Equal(ComplaintAction.Claim);
        ComplaintTransitions.AvailableActions(C(Pending), Admin).Should().Equal(ComplaintAction.Assign, ComplaintAction.Note);
        ComplaintTransitions.AvailableActions(C(InProgress, "a1"), AgentA)
            .Should().Equal(ComplaintAction.Resolve, ComplaintAction.Reject, ComplaintAction.Note);
        ComplaintTransitions.AvailableActions(C(InProgress, "a1"), AgentB).Should().BeEmpty();
        ComplaintTransitions.AvailableActions(C(Resolved, "a1"), Admin).Should().Equal(ComplaintAction.Reopen, ComplaintAction.Note);
    }

    [Fact]
    public void ClosedAndOpenHelpers()
    {
        WellKnown.ComplaintStatuses.IsClosed(Resolved).Should().BeTrue();
        WellKnown.ComplaintStatuses.IsClosed(Rejected).Should().BeTrue();
        WellKnown.ComplaintStatuses.IsOpen(Pending).Should().BeTrue();
        WellKnown.ComplaintStatuses.IsOpen(InProgress).Should().BeTrue();
        WellKnown.ComplaintStatuses.IsClosed(null).Should().BeFalse();
    }
}

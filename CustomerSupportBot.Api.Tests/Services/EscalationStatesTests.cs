// Tests/Services/EscalationStatesTests.cs

using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Api.Services;

namespace CustomerSupportBot.Api.Tests.Services;

public class EscalationStatesTests
{
    private static EscalationRequest NewOpenRequest() => new()
    {
        Status = EscalationStatus.Open,
        CreatedAt = DateTime.UtcNow,
        SessionId = "s1",
        Reason = "test"
    };

    [Fact]
    public void Factory_OpenStatus_ReturnsOpenInstance()
    {
        var state = EscalationStateFactory.Create(EscalationStatus.Open);
        state.Should().BeSameAs(OpenEscalationState.Instance);
        state.Status.Should().Be(EscalationStatus.Open);
    }

    [Fact]
    public void Factory_AllStatuses_ReturnExpectedSingletons()
    {
        EscalationStateFactory.Create(EscalationStatus.Acknowledged)
            .Should().BeSameAs(AcknowledgedEscalationState.Instance);
        EscalationStateFactory.Create(EscalationStatus.Resolved)
            .Should().BeSameAs(ResolvedEscalationState.Instance);
        EscalationStateFactory.Create(EscalationStatus.Dismissed)
            .Should().BeSameAs(DismissedEscalationState.Instance);
    }

    [Fact]
    public void Open_Acknowledge_TransitionsToAcknowledged()
    {
        var req = NewOpenRequest();
        var ok = OpenEscalationState.Instance.Acknowledge(req, "agent42");
        ok.Should().BeTrue();
        req.Status.Should().Be(EscalationStatus.Acknowledged);
        req.AcknowledgedAt.Should().NotBeNull();
        req.AssignedTo.Should().Be("agent42");
    }

    [Fact]
    public void Open_Resolve_TransitionsToResolved()
    {
        var req = NewOpenRequest();
        OpenEscalationState.Instance.Resolve(req, "agent42", "fixed it").Should().BeTrue();
        req.Status.Should().Be(EscalationStatus.Resolved);
        req.Resolution.Should().Be("fixed it");
    }

    [Fact]
    public void Open_Dismiss_TransitionsToDismissed()
    {
        var req = NewOpenRequest();
        OpenEscalationState.Instance.Dismiss(req, "agent42", null).Should().BeTrue();
        req.Status.Should().Be(EscalationStatus.Dismissed);
    }

    [Fact]
    public void Acknowledged_ReAcknowledge_Rejected()
    {
        var req = NewOpenRequest();
        req.Status = EscalationStatus.Acknowledged;
        AcknowledgedEscalationState.Instance.Acknowledge(req, "x").Should().BeFalse();
        req.Status.Should().Be(EscalationStatus.Acknowledged);
    }

    [Fact]
    public void Acknowledged_Resolve_AllowsTransition()
    {
        var req = NewOpenRequest();
        req.Status = EscalationStatus.Acknowledged;
        AcknowledgedEscalationState.Instance.Resolve(req, "x", "ok").Should().BeTrue();
        req.Status.Should().Be(EscalationStatus.Resolved);
    }

    [Fact]
    public void Resolved_AnyTransition_Rejected()
    {
        var req = NewOpenRequest();
        req.Status = EscalationStatus.Resolved;
        ResolvedEscalationState.Instance.Acknowledge(req, "x").Should().BeFalse();
        ResolvedEscalationState.Instance.Resolve(req, "x", "y").Should().BeFalse();
        ResolvedEscalationState.Instance.Dismiss(req, "x", "y").Should().BeFalse();
        req.Status.Should().Be(EscalationStatus.Resolved);
    }

    [Fact]
    public void Dismissed_AnyTransition_Rejected()
    {
        var req = NewOpenRequest();
        req.Status = EscalationStatus.Dismissed;
        DismissedEscalationState.Instance.Acknowledge(req, "x").Should().BeFalse();
        DismissedEscalationState.Instance.Resolve(req, "x", "y").Should().BeFalse();
        DismissedEscalationState.Instance.Dismiss(req, "x", "y").Should().BeFalse();
    }
}

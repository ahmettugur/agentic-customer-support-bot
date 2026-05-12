// Tests/Services/InMemoryEscalationSinkTests.cs

using CustomerSupportBot.Api.Models;
using CustomerSupportBot.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Api.Tests.Services;

public class InMemoryEscalationSinkTests
{
    private readonly InMemoryEscalationSink _sink = new(NullLogger<InMemoryEscalationSink>.Instance);

    private static EscalationRequest NewReq(string id = "e1") => new()
    {
        Id = id,
        SessionId = "s1",
        AgentName = "ComplaintAgent",
        Reason = "needs human",
        UserQuery = "şikayet",
        Status = EscalationStatus.Open,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public void Create_StoresAndFiresEvent()
    {
        EscalationRequest? captured = null;
        _sink.RequestCreated += (_, r) => captured = r;
        var req = _sink.Create(NewReq());
        captured.Should().BeSameAs(req);
        _sink.Get("e1").Should().BeSameAs(req);
    }

    [Fact]
    public void GetOpen_FiltersOpenAndAcknowledged()
    {
        var open = _sink.Create(NewReq("o1"));
        var ack = _sink.Create(NewReq("o2"));
        ack.Status = EscalationStatus.Acknowledged;
        var resolved = _sink.Create(NewReq("o3"));
        resolved.Status = EscalationStatus.Resolved;

        _sink.GetOpen().Should().HaveCount(2);
    }

    [Fact]
    public void Decide_AcknowledgeOpen_Transitions()
    {
        _sink.Create(NewReq("e1"));
        _sink.Decide("e1", "acknowledge", "agent42").Should().BeTrue();
        _sink.Get("e1")!.Status.Should().Be(EscalationStatus.Acknowledged);
    }

    [Fact]
    public void Decide_Resolve_TransitionsAndCapturesResolution()
    {
        _sink.Create(NewReq("e1"));
        _sink.Decide("e1", "resolve", "agent42", "fixed").Should().BeTrue();
        var req = _sink.Get("e1")!;
        req.Status.Should().Be(EscalationStatus.Resolved);
        req.Resolution.Should().Be("fixed");
    }

    [Fact]
    public void Decide_Dismiss_Transitions()
    {
        _sink.Create(NewReq("e1"));
        _sink.Decide("e1", "dismiss", "agent42", null).Should().BeTrue();
        _sink.Get("e1")!.Status.Should().Be(EscalationStatus.Dismissed);
    }

    [Fact]
    public void Decide_UnknownAction_False()
    {
        _sink.Create(NewReq("e1"));
        _sink.Decide("e1", "weird", null, null).Should().BeFalse();
    }

    [Fact]
    public void Decide_UnknownId_False()
    {
        _sink.Decide("nope", "resolve", null, null).Should().BeFalse();
    }

    [Fact]
    public void Decide_OnTerminalState_False()
    {
        _sink.Create(NewReq("e1"));
        _sink.Decide("e1", "resolve", null, "x");
        _sink.Decide("e1", "acknowledge", null, null).Should().BeFalse();
    }

    [Fact]
    public void GetRecent_ReturnsByDescCreated()
    {
        _sink.Create(NewReq("a"));
        Thread.Sleep(5);
        _sink.Create(NewReq("b"));
        var recent = _sink.GetRecent(10);
        recent[0].Id.Should().Be("b");
        recent[1].Id.Should().Be("a");
    }
}

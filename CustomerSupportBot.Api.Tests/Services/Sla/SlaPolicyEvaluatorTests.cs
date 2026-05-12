// Services/Sla/SlaPolicyEvaluatorTests.cs
// SLA evaluator için saf birim testler — kuyruk girdileri + sink + now değeri
// → beklenen warn/breach/aksiyon. BackgroundService gerektirmez.

using CustomerSupportBot.Api.Models;
using CustomerSupportBot.Api.Services.Sla;
using CustomerSupportBot.Api.Models;
using CustomerSupportBot.Api.Services.Sla;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Tests.Services.Sla;

public class SlaPolicyEvaluatorTests
{
    private static InMemorySlaEventSink NewSink() =>
        new(NullLogger<InMemorySlaEventSink>.Instance);

    private static ApprovalRequest NewApproval(int ageSeconds, string? id = null) =>
        new()
        {
            Id = id ?? "appr-1",
            ToolName = "order_placement_tool",
            RequestedAt = DateTime.UtcNow.AddSeconds(-ageSeconds)
        };

    private static EscalationRequest NewEscalation(int ageSeconds, EscalationPriority priority = EscalationPriority.Normal) =>
        new()
        {
            Id = "esc-1",
            CreatedAt = DateTime.UtcNow.AddSeconds(-ageSeconds),
            Priority = priority
        };

    [Fact]
    public void Approval_Below_Warn_NoEvent()
    {
        var sink = NewSink();
        var opts = new ApprovalSlaOptions { WarnAfterSeconds = 20, BreachAfterSeconds = 45 };

        var result = SlaPolicyEvaluator.EvaluateApproval(
            NewApproval(5), opts, sink, DateTime.UtcNow);

        result.WarnEvent.Should().BeNull();
        result.BreachEvent.Should().BeNull();
        result.BreachAction.Should().Be(SlaBreachAction.None);
    }

    [Fact]
    public void Approval_Above_Warn_BelowBreach_EmitsWarn()
    {
        var sink = NewSink();
        var opts = new ApprovalSlaOptions { WarnAfterSeconds = 20, BreachAfterSeconds = 45 };

        var result = SlaPolicyEvaluator.EvaluateApproval(
            NewApproval(25), opts, sink, DateTime.UtcNow);

        result.WarnEvent.Should().NotBeNull();
        result.WarnEvent!.Severity.Should().Be(SlaPolicyEvaluator.SeverityWarn);
        result.WarnEvent.AgeSeconds.Should().BeGreaterThanOrEqualTo(25);
        result.BreachEvent.Should().BeNull();
    }

    [Fact]
    public void Approval_AboveBreach_EmitsBreach_WithAutoReject()
    {
        var sink = NewSink();
        var opts = new ApprovalSlaOptions
        {
            WarnAfterSeconds = 20,
            BreachAfterSeconds = 45,
            OnBreach = SlaBreachAction.AutoReject
        };

        var result = SlaPolicyEvaluator.EvaluateApproval(
            NewApproval(50), opts, sink, DateTime.UtcNow);

        result.BreachEvent.Should().NotBeNull();
        result.BreachAction.Should().Be(SlaBreachAction.AutoReject);
        result.BreachEvent!.Action.Should().Be("AutoReject");
    }

    [Fact]
    public void Approval_Breach_NotReEmitted_Once_Recorded()
    {
        var sink = NewSink();
        var opts = new ApprovalSlaOptions { BreachAfterSeconds = 10 };
        var req = NewApproval(15);

        var first = SlaPolicyEvaluator.EvaluateApproval(req, opts, sink, DateTime.UtcNow);
        first.BreachEvent.Should().NotBeNull();
        sink.Record(first.BreachEvent!);

        var second = SlaPolicyEvaluator.EvaluateApproval(req, opts, sink, DateTime.UtcNow);
        second.BreachEvent.Should().BeNull();
    }

    [Fact]
    public void Escalation_Breach_BoostsPriority()
    {
        var sink = NewSink();
        var opts = new EscalationSlaOptions
        {
            WarnAfterSeconds = 30,
            BreachAfterSeconds = 60,
            BoostPriorityOnBreach = true
        };

        var result = SlaPolicyEvaluator.EvaluateEscalation(
            NewEscalation(70, EscalationPriority.Normal), opts, sink, DateTime.UtcNow);

        result.BreachEvent.Should().NotBeNull();
        result.NewPriority.Should().Be(EscalationPriority.High);
    }

    [Fact]
    public void Escalation_Breach_PriorityDisabled_NoBoost()
    {
        var sink = NewSink();
        var opts = new EscalationSlaOptions
        {
            BreachAfterSeconds = 60,
            BoostPriorityOnBreach = false
        };

        var result = SlaPolicyEvaluator.EvaluateEscalation(
            NewEscalation(70, EscalationPriority.Low), opts, sink, DateTime.UtcNow);

        result.BreachEvent.Should().NotBeNull();
        result.NewPriority.Should().BeNull();
    }

    [Fact]
    public void BoostPriority_Critical_StaysCritical()
    {
        SlaPolicyEvaluator.BoostPriority(EscalationPriority.Critical)
            .Should().Be(EscalationPriority.Critical);
    }

    [Fact]
    public void BoostPriority_Low_To_Normal()
    {
        SlaPolicyEvaluator.BoostPriority(EscalationPriority.Low)
            .Should().Be(EscalationPriority.Normal);
    }

    [Fact]
    public void Sink_Records_And_DeDupes_By_Key()
    {
        var sink = NewSink();
        var evt = new SlaEvent
        {
            Kind = SlaPolicyEvaluator.KindApproval,
            Severity = SlaPolicyEvaluator.SeverityWarn,
            TargetId = "appr-1",
            AgeSeconds = 25
        };

        sink.Record(evt);

        sink.LastEmittedAt(SlaPolicyEvaluator.KindApproval, "appr-1", SlaPolicyEvaluator.SeverityWarn)
            .Should().NotBeNull();
        sink.LastEmittedAt(SlaPolicyEvaluator.KindApproval, "appr-1", SlaPolicyEvaluator.SeverityBreach)
            .Should().BeNull();
        sink.GetRecent().Should().ContainSingle(e => e.TargetId == "appr-1");
    }

    [Fact]
    public void Sink_Caps_At_500_Items()
    {
        var sink = NewSink();
        for (var i = 0; i < 600; i++)
        {
            sink.Record(new SlaEvent { Kind = "k", Severity = "warn", TargetId = $"t-{i}" });
        }

        sink.GetRecent(1000).Count.Should().BeLessThanOrEqualTo(500);
    }
}

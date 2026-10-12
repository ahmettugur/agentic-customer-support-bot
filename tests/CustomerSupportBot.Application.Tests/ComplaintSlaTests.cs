// Şikayet SLA: Beklemede'de üstlenme eşiği, açık kalma eşiği; her olay bir kez.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Sla;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class ComplaintSlaTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
    private static readonly ComplaintSlaOptions O = new()
    {
        PickupWarnAfterSeconds = 100, PickupBreachAfterSeconds = 200, ResolveWarnAfterSeconds = 1000, ResolveBreachAfterSeconds = 2000
    };

    private static ComplaintInfo C(string status, int ageSeconds) => new()
    {
        OrderId = "1030", CustomerId = "1001", Complaint = "x", Status = status, CreatedAt = Now.AddSeconds(-ageSeconds)
    };

    [Fact]
    public async Task Pending_PastPickupBreach_EmitsPickupBreach_Once()
    {
        var sink = new InMemorySlaEventSink(NullLogger<InMemorySlaEventSink>.Instance);

        var first = SlaPolicyEvaluator.EvaluateComplaint("1006", C(WellKnown.ComplaintStatuses.Pending, 250), O, sink, Now);
        first.Should().ContainSingle(e => e.Kind == SlaPolicyEvaluator.KindComplaintPickup && e.Severity == SlaPolicyEvaluator.SeverityBreach);
        foreach (var e in first) await sink.RecordAsync(e);

        SlaPolicyEvaluator.EvaluateComplaint("1006", C(WellKnown.ComplaintStatuses.Pending, 260), O, sink, Now).Should().BeEmpty();
    }

    [Fact]
    public void InProgress_IgnoresPickup_UsesResolveWarn()
    {
        var events = SlaPolicyEvaluator.EvaluateComplaint("1006", C(WellKnown.ComplaintStatuses.InProgress, 1500), O, new InMemorySlaEventSink(NullLogger<InMemorySlaEventSink>.Instance), Now);

        events.Should().ContainSingle().Which.Should().Match<SlaEvent>(e =>
            e.Kind == SlaPolicyEvaluator.KindComplaintResolve && e.Severity == SlaPolicyEvaluator.SeverityWarn && e.TargetId == "1006");
    }

    [Fact]
    public void Young_Or_Legacy_NoEvents()
    {
        SlaPolicyEvaluator.EvaluateComplaint("1", C(WellKnown.ComplaintStatuses.Pending, 10), O, new InMemorySlaEventSink(NullLogger<InMemorySlaEventSink>.Instance), Now).Should().BeEmpty();
        SlaPolicyEvaluator.EvaluateComplaint("2", new ComplaintInfo { Status = WellKnown.ComplaintStatuses.Pending }, O, new InMemorySlaEventSink(NullLogger<InMemorySlaEventSink>.Instance), Now)
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData(WellKnown.ComplaintStatuses.Pending, 150, "warn")]
    [InlineData(WellKnown.ComplaintStatuses.Pending, 250, "breach")]
    [InlineData(WellKnown.ComplaintStatuses.InProgress, 250, "ok")]
    [InlineData(WellKnown.ComplaintStatuses.InProgress, 2500, "breach")]
    [InlineData(WellKnown.ComplaintStatuses.Resolved, 9999, "none")]
    public void Tone(string status, int age, string expected)
        => SlaPolicyEvaluator.ComplaintTone(C(status, age), O, Now).Should().Be(expected);

    [Fact]
    public async Task Scan_RecordsComplaintEvents_AndStatusReportsComplaints()
    {
        var sink = new InMemorySlaEventSink(NullLogger<InMemorySlaEventSink>.Instance);
        var repo = Substitute.For<IComplaintRepository>();
        repo.GetOpenAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
        [
            new ComplaintListItem("1006", C(WellKnown.ComplaintStatuses.Pending, 3 * 86400)),
            new ComplaintListItem("1007", C(WellKnown.ComplaintStatuses.InProgress, 60))
        ]);
        var approvals = Substitute.For<IApprovalQueue>();
        approvals.GetPendingAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ApprovalRequest>());
        var escalations = Substitute.For<IEscalationSink>();
        escalations.GetOpen().Returns(Array.Empty<EscalationRequest>());
        var opts = new SlaOptions();
        var svc = new SlaPortService(sink, approvals, escalations, new FixedOptionsMonitor<SlaOptions>(opts), repo);

        await svc.ScanOnceAsync(opts, TestContext.Current.CancellationToken);

        sink.GetRecent(50).Should().Contain(e => e.TargetId == "1006" && e.Kind == SlaPolicyEvaluator.KindComplaintPickup);
        var status = await svc.GetStatusAsync(TestContext.Current.CancellationToken);
        status.Complaints!.OpenCount.Should().Be(2);
        status.Complaints.PendingCount.Should().Be(1);
        status.Complaints.OldestSeconds.Should().BeGreaterThanOrEqualTo(3 * 86400 - 5);
    }
}

file sealed class FixedOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

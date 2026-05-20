// Services/Sla/SlaGuardianServiceTests.cs
// Guardian'�n ger�ek InMemoryApprovalQueue + InMemoryEscalationSink �zerinde
// ScanOnce davran���n� do�rular: warn/breach kay�tlar�, AutoReject ve
// �ncelik y�kseltmesi.

using CustomerSupportBot.Api.Workers;
using CustomerSupportBot.Application.Services;
using CustomerSupportBot.Application.Services.Sla;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Tests.Services.Sla;

public class SlaGuardianServiceTests
{
    private static (SlaGuardianService svc, IServiceProvider sp,
                    InMemoryApprovalQueue approvals, InMemoryEscalationSink escalations,
                    InMemorySlaEventSink sink)
        BuildHarness(SlaOptions opts)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<ApprovalOptions>(o => o.TimeoutSeconds = 600);
        services.AddSingleton<IApprovalQueue, InMemoryApprovalQueue>();
        services.AddSingleton<IEscalationSink, InMemoryEscalationSink>();
        services.AddSingleton<ISlaEventSink, InMemorySlaEventSink>();

        var sp = services.BuildServiceProvider();
        var monitor = new TestOptionsMonitor<SlaOptions>(opts);
        var guardian = new SlaGuardianService(sp, monitor, NullLogger<SlaGuardianService>.Instance);

        var approvals = (InMemoryApprovalQueue)sp.GetRequiredService<IApprovalQueue>();
        var escalations = (InMemoryEscalationSink)sp.GetRequiredService<IEscalationSink>();
        var sink = (InMemorySlaEventSink)sp.GetRequiredService<ISlaEventSink>();
        return (guardian, sp, approvals, escalations, sink);
    }

    [Fact]
    public void ScanOnce_Approval_BreachesAndAutoRejects()
    {
        var opts = new SlaOptions
        {
            Approvals = new ApprovalSlaOptions
            {
                WarnAfterSeconds = 1,
                BreachAfterSeconds = 2,
                OnBreach = SlaBreachAction.AutoReject
            }
        };
        var (svc, _, approvals, _, sink) = BuildHarness(opts);

        var req = approvals.Create(new ApprovalRequest
        {
            ToolName = "order_placement_tool",
            RequestedAt = DateTime.UtcNow.AddSeconds(-10),
            SessionId = "s1"
        });

        svc.ScanOnce(opts);

        sink.GetRecent().Should().Contain(e =>
            e.Severity == SlaPolicyEvaluator.SeverityBreach &&
            e.TargetId == req.Id);

        approvals.Get(req.Id)!.Status.Should().Be(ApprovalStatus.Rejected);
    }

    [Fact]
    public void ScanOnce_Approval_AboveWarn_BelowBreach_OnlyWarn()
    {
        var opts = new SlaOptions
        {
            Approvals = new ApprovalSlaOptions
            {
                WarnAfterSeconds = 2,
                BreachAfterSeconds = 100,
                OnBreach = SlaBreachAction.AutoReject
            }
        };
        var (svc, _, approvals, _, sink) = BuildHarness(opts);

        var req = approvals.Create(new ApprovalRequest
        {
            ToolName = "complaint_registration_tool",
            RequestedAt = DateTime.UtcNow.AddSeconds(-5),
            SessionId = "s1"
        });

        svc.ScanOnce(opts);

        var events = sink.GetRecent();
        events.Should().Contain(e => e.Severity == SlaPolicyEvaluator.SeverityWarn);
        events.Should().NotContain(e => e.Severity == SlaPolicyEvaluator.SeverityBreach);
        approvals.Get(req.Id)!.Status.Should().Be(ApprovalStatus.Pending);
    }

    [Fact]
    public void ScanOnce_Escalation_Breach_BoostsPriority()
    {
        var opts = new SlaOptions
        {
            Escalations = new EscalationSlaOptions
            {
                WarnAfterSeconds = 1,
                BreachAfterSeconds = 2,
                BoostPriorityOnBreach = true
            }
        };
        var (svc, _, _, escalations, sink) = BuildHarness(opts);

        var esc = escalations.Create(new EscalationRequest
        {
            CreatedAt = DateTime.UtcNow.AddSeconds(-5),
            Priority = EscalationPriority.Normal,
            UserQuery = "x",
            Reason = "y"
        });

        svc.ScanOnce(opts);

        escalations.Get(esc.Id)!.Priority.Should().Be(EscalationPriority.High);
        sink.GetRecent().Should().Contain(e =>
            e.Kind == SlaPolicyEvaluator.KindEscalation &&
            e.Severity == SlaPolicyEvaluator.SeverityBreach);
    }

    [Fact]
    public void ScanOnce_Idempotent_DoesNotDuplicateBreachEvents()
    {
        var opts = new SlaOptions
        {
            Approvals = new ApprovalSlaOptions
            {
                BreachAfterSeconds = 1,
                OnBreach = SlaBreachAction.None
            }
        };
        var (svc, _, approvals, _, sink) = BuildHarness(opts);

        approvals.Create(new ApprovalRequest
        {
            ToolName = "order_placement_tool",
            RequestedAt = DateTime.UtcNow.AddSeconds(-5)
        });

        svc.ScanOnce(opts);
        svc.ScanOnce(opts);
        svc.ScanOnce(opts);

        var breachEvents = sink.GetRecent().Where(e =>
            e.Severity == SlaPolicyEvaluator.SeverityBreach).ToList();

        breachEvents.Should().HaveCount(1);
    }

    private class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public TestOptionsMonitor(T value) { CurrentValue = value; }
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}

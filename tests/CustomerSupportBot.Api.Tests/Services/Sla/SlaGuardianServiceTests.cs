// Services/Sla/SlaGuardianServiceTests.cs
// ISlaPort.ScanOnceAsync davranışını doğrular: warn/breach kayıtları, AutoReject ve
// öncelik yükseltmesi. SlaPortService (Application katmanı) doğrudan test edilir;
// SlaGuardianService artık ince bir tetikleyici olduğundan ayrıca test edilmez.

using CustomerSupportBot.Api.Tests.Helpers;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Services.Sla;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Tests.Services.Sla;

public class SlaGuardianServiceTests
{
    private static (ISlaPort slaPort,
                    InMemoryApprovalQueue approvals, InMemoryEscalationSink escalations,
                    InMemorySlaEventSink sink)
        BuildHarness(SlaOptions opts)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<ApprovalOptions>(o => o.TimeoutSeconds = 600);
        services.AddSingleton<IApprovalExecutionRouter, NoopApprovalExecutionRouter>();
        services.AddSingleton<IApprovalQueue, InMemoryApprovalQueue>();
        services.AddSingleton<IEscalationSink, InMemoryEscalationSink>();
        services.AddSingleton<ISlaEventSink, InMemorySlaEventSink>();

        var sp = services.BuildServiceProvider();
        var monitor = new TestOptionsMonitor<SlaOptions>(opts);

        var slaPort = new SlaPortService(
            sp.GetRequiredService<ISlaEventSink>(),
            sp.GetRequiredService<IApprovalQueue>(),
            sp.GetRequiredService<IEscalationSink>(),
            monitor);

        var approvals = (InMemoryApprovalQueue)sp.GetRequiredService<IApprovalQueue>();
        var escalations = (InMemoryEscalationSink)sp.GetRequiredService<IEscalationSink>();
        var sink = (InMemorySlaEventSink)sp.GetRequiredService<ISlaEventSink>();
        return (slaPort, approvals, escalations, sink);
    }

    [Fact]
    public async Task ScanOnce_Approval_BreachesAndAutoRejects()
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
        var (slaPort, approvals, _, sink) = BuildHarness(opts);

        var req = await approvals.CreateAsync(new ApprovalRequest
        {
            ToolName = "order_placement_tool",
            RequestedAt = DateTime.UtcNow.AddSeconds(-10),
            SessionId = "s1"
        }, TestContext.Current.CancellationToken);

        await slaPort.ScanOnceAsync(opts, TestContext.Current.CancellationToken);

        sink.GetRecent().Should().Contain(e =>
            e.Severity == SlaPolicyEvaluator.SeverityBreach &&
            e.TargetId == req.Id);

        approvals.Get(req.Id)!.Status.Should().Be(ApprovalStatus.Rejected);
    }

    [Fact]
    public async Task ScanOnce_Approval_DefaultOptions_BreachDoesNotAutoReject()
    {
        // REGRESYON: ApprovalSlaOptions.OnBreach'in varsayılanı AutoReject idi. Bu, HITL
        // bloklamayan modele taşınmadan (tool çağrısı artık admin kararını beklemiyor,
        // bkz. ApprovalGateService.ExecuteWithApprovalGateAsync) önceki tasarımdan kalma bir
        // artıktı — tasarım değişirken güncellenmemişti. Sonuç: appsettings.json'daki
        // Sla.Approvals (BreachAfterSeconds=60, o zaman OnBreach=AutoReject) her bekleyen
        // onayı admin hiç bakmasa bile 60. saniyede sessizce reddediyordu; bloklamayan
        // modelin "admin ne zaman bakarsa baksın" amacını fiilen geçersiz kılıyordu.
        // Explicit OnBreach set etmeden — yani gerçek varsayılanı kullanarak — test ediliyor.
        var opts = new SlaOptions
        {
            Approvals = new ApprovalSlaOptions { WarnAfterSeconds = 1, BreachAfterSeconds = 2 }
        };
        var (slaPort, approvals, _, sink) = BuildHarness(opts);

        var req = await approvals.CreateAsync(new ApprovalRequest
        {
            ToolName = "order_placement_tool",
            RequestedAt = DateTime.UtcNow.AddSeconds(-10),
            SessionId = "s1"
        }, TestContext.Current.CancellationToken);

        await slaPort.ScanOnceAsync(opts, TestContext.Current.CancellationToken);

        sink.GetRecent().Should().Contain(e =>
            e.Severity == SlaPolicyEvaluator.SeverityBreach && e.TargetId == req.Id,
            "admin panelinde 'uzun süredir bekliyor' uyarısı hâlâ görünmeli");
        approvals.Get(req.Id)!.Status.Should().Be(ApprovalStatus.Pending,
            "SLA breach yalnızca bir uyarı — kararı admin verir, sistem değil");
    }

    [Fact]
    public async Task ScanOnce_Approval_AboveWarn_BelowBreach_OnlyWarn()
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
        var (slaPort, approvals, _, sink) = BuildHarness(opts);

        var req = await approvals.CreateAsync(new ApprovalRequest
        {
            ToolName = "complaint_registration_tool",
            RequestedAt = DateTime.UtcNow.AddSeconds(-5),
            SessionId = "s1"
        }, TestContext.Current.CancellationToken);

        await slaPort.ScanOnceAsync(opts, TestContext.Current.CancellationToken);

        var events = sink.GetRecent();
        events.Should().Contain(e => e.Severity == SlaPolicyEvaluator.SeverityWarn);
        events.Should().NotContain(e => e.Severity == SlaPolicyEvaluator.SeverityBreach);
        approvals.Get(req.Id)!.Status.Should().Be(ApprovalStatus.Pending);
    }

    [Fact]
    public async Task ScanOnce_Escalation_Breach_BoostsPriority()
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
        var (slaPort, _, escalations, sink) = BuildHarness(opts);

        var esc = escalations.Create(new EscalationRequest
        {
            CreatedAt = DateTime.UtcNow.AddSeconds(-5),
            Priority = EscalationPriority.Normal,
            UserQuery = "x",
            Reason = "y"
        });

        await slaPort.ScanOnceAsync(opts, TestContext.Current.CancellationToken);

        escalations.Get(esc.Id)!.Priority.Should().Be(EscalationPriority.High);
        sink.GetRecent().Should().Contain(e =>
            e.Kind == SlaPolicyEvaluator.KindEscalation &&
            e.Severity == SlaPolicyEvaluator.SeverityBreach);
    }

    [Fact]
    public async Task ScanOnce_Idempotent_DoesNotDuplicateBreachEvents()
    {
        var opts = new SlaOptions
        {
            Approvals = new ApprovalSlaOptions
            {
                BreachAfterSeconds = 1,
                OnBreach = SlaBreachAction.None
            }
        };
        var (slaPort, approvals, _, sink) = BuildHarness(opts);

        await approvals.CreateAsync(new ApprovalRequest
        {
            ToolName = "order_placement_tool",
            RequestedAt = DateTime.UtcNow.AddSeconds(-5)
        }, TestContext.Current.CancellationToken);

        await slaPort.ScanOnceAsync(opts, TestContext.Current.CancellationToken);
        await slaPort.ScanOnceAsync(opts, TestContext.Current.CancellationToken);
        await slaPort.ScanOnceAsync(opts, TestContext.Current.CancellationToken);

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

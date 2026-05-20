using CustomerSupportBot.Application.Ports.Driven.Persistence;
using CustomerSupportBot.Application.Services.Sla;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Workers;

public class SlaGuardianService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<SlaOptions> _options;
    private readonly ILogger<SlaGuardianService> _logger;

    public SlaGuardianService(
        IServiceProvider services,
        IOptionsMonitor<SlaOptions> options,
        ILogger<SlaGuardianService> logger)
    {
        _services = services;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var initial = _options.CurrentValue;
        if (!initial.Enabled)
        {
            _logger.LogInformation("[SLA] Guardian disabled — exiting.");
            return;
        }

        _logger.LogInformation(
            "[SLA] Guardian started. Poll={Poll}s, ApprovalBreach={ApprBreach}s, EscalationBreach={EscBreach}s",
            initial.PollIntervalSeconds,
            initial.Approvals.BreachAfterSeconds,
            initial.Escalations.BreachAfterSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var opts = _options.CurrentValue;
            try
            {
                ScanOnce(opts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SLA] Scan iteration failed");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(Math.Max(1, opts.PollIntervalSeconds)),
                    stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Tek bir tarama döngüsü — testlerin de doğrudan çağırabilmesi için public.</summary>
    public void ScanOnce(SlaOptions opts)
    {
        using var scope = _services.CreateScope();
        var sink = scope.ServiceProvider.GetRequiredService<ISlaEventSink>();
        var approvals = scope.ServiceProvider.GetRequiredService<IApprovalQueue>();
        var escalations = scope.ServiceProvider.GetRequiredService<IEscalationSink>();

        var now = DateTime.UtcNow;

        foreach (var req in approvals.GetPending())
        {
            var eval = SlaPolicyEvaluator.EvaluateApproval(req, opts.Approvals, sink, now);
            if (eval.WarnEvent is not null) sink.Record(eval.WarnEvent);
            if (eval.BreachEvent is not null)
            {
                sink.Record(eval.BreachEvent);
                ApplyApprovalBreach(approvals, req, eval.BreachAction);
            }
        }

        foreach (var esc in escalations.GetOpen())
        {
            var eval = SlaPolicyEvaluator.EvaluateEscalation(esc, opts.Escalations, sink, now);
            if (eval.WarnEvent is not null) sink.Record(eval.WarnEvent);
            if (eval.BreachEvent is not null)
            {
                sink.Record(eval.BreachEvent);
                if (eval.NewPriority.HasValue && eval.NewPriority.Value != esc.Priority)
                    esc.Priority = eval.NewPriority.Value;
            }
        }
    }

    private void ApplyApprovalBreach(
        IApprovalQueue queue,
        ApprovalRequest req,
        SlaBreachAction action)
    {
        switch (action)
        {
            case SlaBreachAction.AutoReject:
                queue.Decide(req.Id, approved: false,
                    decidedBy: WellKnown.Defaults.System,
                    reason: "SLA breach — auto-reject");
                break;
            case SlaBreachAction.AutoApprove:
                queue.Decide(req.Id, approved: true,
                    decidedBy: WellKnown.Defaults.System,
                    reason: "SLA breach — auto-approve");
                break;
            case SlaBreachAction.None:
            default:
                break;
        }
    }
}

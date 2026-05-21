using CustomerSupportBot.Application.Ports.Driving;
using CustomerSupportBot.Application.Services.Sla;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Workers;

/// <summary>
/// Hosting adapter — periyodik SLA taramasını tetikler.
/// İş mantığı ISlaPort.ScanOnce() içinde (Application katmanı).
/// </summary>
public class SlaGuardianService : BackgroundService
{
    private readonly ISlaPort _slaPort;
    private readonly IOptionsMonitor<SlaOptions> _options;
    private readonly ILogger<SlaGuardianService> _logger;

    public SlaGuardianService(
        ISlaPort slaPort,
        IOptionsMonitor<SlaOptions> options,
        ILogger<SlaGuardianService> logger)
    {
        _slaPort = slaPort;
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
                _slaPort.ScanOnce(opts);
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
}

using CustomerSupportBot.Application.Ports.Outbound.Locking;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Services.Sla;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Workers;

/// <summary>
/// Hosting adapter — periyodik SLA taramasını tetikler.
/// İş mantığı ISlaPort.ScanOnce() içinde (Application katmanı).
/// Multi-pod ortamında çift yürütmeyi önlemek için IAppDistributedLock kullanılır.
/// Redis yoksa (null lock) her pod kendi taramasını yapar — single-instance davranışı korunur.
/// </summary>
public class SlaGuardianService : BackgroundService
{
    private const string LockKey = "sla:guardian:scan";

    private readonly ISlaPort _slaPort;
    private readonly IOptionsMonitor<SlaOptions> _options;
    private readonly ILogger<SlaGuardianService> _logger;
    private readonly IAppDistributedLock? _distributedLock;

    public SlaGuardianService(
        ISlaPort slaPort,
        IOptionsMonitor<SlaOptions> options,
        ILogger<SlaGuardianService> logger,
        IAppDistributedLock? distributedLock = null)
    {
        _slaPort = slaPort;
        _options = options;
        _logger = logger;
        _distributedLock = distributedLock;
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
            "[SLA] Guardian started. Poll={Poll}s, ApprovalBreach={ApprBreach}s, EscalationBreach={EscBreach}s, DistributedLock={Lock}",
            initial.PollIntervalSeconds,
            initial.Approvals.BreachAfterSeconds,
            initial.Escalations.BreachAfterSeconds,
            _distributedLock != null ? "Redis" : "none (single-instance)");

        while (!stoppingToken.IsCancellationRequested)
        {
            var opts = _options.CurrentValue;
            try
            {
                await RunScanWithLockAsync(opts, stoppingToken);
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

    private async Task RunScanWithLockAsync(SlaOptions opts, CancellationToken ct)
    {
        if (_distributedLock == null)
        {
            // Redis yok — single-instance, kilit gereksiz
            await _slaPort.ScanOnceAsync(opts, ct);
            return;
        }

        // Kilit süresi: bir poll interval'inden kısa tut; başka pod scan başlatmasın.
        var lockTtl = TimeSpan.FromSeconds(Math.Max(1, opts.PollIntervalSeconds - 1));
        await using var handle = await _distributedLock.TryAcquireAsync(LockKey, lockTtl, ct);

        if (handle == null)
        {
            _logger.LogDebug("[SLA] Distributed lock alınamadı — bu pod taramayı atlıyor.");
            return;
        }

        await _slaPort.ScanOnceAsync(opts, ct);
    }
}

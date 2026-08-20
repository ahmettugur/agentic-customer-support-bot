// Adapters.Persistence/EfCore/StaleApprovalSweepService.cs
// Bloklamayan onay modelinde (bkz. ApprovalGateService) tool çağrısı artık admin kararını
// beklemiyor, bu yüzden IApprovalQueue.AwaitDecisionAsync/TimeoutSeconds bu 4 tool için hiç
// tetiklenmiyor. Bu periyodik sweep, ApprovalOptions.StalePendingHours'ı aşan Pending kayıtları
// otomatik reddeder — DecideAsync üzerinden, yani normal bildirim/SSE akışı aynen çalışır.

using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.Persistence.EfCore;

public sealed class StaleApprovalSweepService : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(15);

    private readonly IApprovalQueue _approvals;
    private readonly ApprovalOptions _options;
    private readonly ILogger<StaleApprovalSweepService> _logger;

    public StaleApprovalSweepService(
        IApprovalQueue approvals,
        IOptions<ApprovalOptions> options,
        ILogger<StaleApprovalSweepService> logger)
    {
        _approvals = approvals;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HITL] Stale approval sweep başarısız.");
            }

            try
            {
                await Task.Delay(SweepInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Uygulama kapanıyor.
            }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - TimeSpan.FromHours(_options.StalePendingHours);
        // Kalıcı okuma: süpürgenin göremediği talep hiçbir zaman zaman aşımına uğramaz
        // ve sonsuza kadar bekler.
        var pending = await _approvals.GetPendingAsync(ct);
        var stale = pending.Where(r => r.RequestedAt < cutoff).ToList();

        foreach (var req in stale)
        {
            ct.ThrowIfCancellationRequested();
            var decided = await _approvals.DecideAsync(
                req.Id,
                approved: false,
                decidedBy: WellKnown.Defaults.System,
                reason: "stale",
                ct: ct).ConfigureAwait(false);

            if (decided)
                _logger.LogWarning(
                    "[HITL] Stale approval otomatik reddedildi. Id={Id} Tool={Tool} RequestedAt={RequestedAt}",
                    req.Id, req.ToolName, req.RequestedAt);
        }
    }
}

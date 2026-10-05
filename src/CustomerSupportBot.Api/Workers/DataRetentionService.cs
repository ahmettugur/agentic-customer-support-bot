using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Locking;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Workers;

/// <summary>
/// Hosting adapter — kişisel veri saklama süresi taramasını (<c>DataRetention</c>) periyodik tetikler.
/// İş mantığı <see cref="IDataPrivacyPort.RunRetentionAsync"/> içinde (Application katmanı).
///
/// <para>
/// Çok pod'lu kurulumda aynı anda tek pod tarar: kilit beklenmeden denenir; başka bir pod taramadaysa bu
/// pod o turu atlar. Silme tekrarlanabilir olduğu için iki pod'un art arda taraması zararsızdır, yalnızca
/// gereksiz yük olurdu. Redis yoksa (kilit yok) her pod kendi taramasını yapar.
/// </para>
/// </summary>
public sealed class DataRetentionService : BackgroundService
{
    internal const string LockKey = "privacy:retention:sweep";

    private readonly IDataPrivacyPort _privacy;
    private readonly IOptionsMonitor<DataRetentionOptions> _options;
    private readonly ILogger<DataRetentionService> _logger;
    private readonly IAppDistributedLock? _distributedLock;

    public DataRetentionService(
        IDataPrivacyPort privacy,
        IOptionsMonitor<DataRetentionOptions> options,
        ILogger<DataRetentionService> logger,
        IAppDistributedLock? distributedLock = null)
    {
        _privacy = privacy;
        _options = options;
        _logger = logger;
        _distributedLock = distributedLock;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var initial = _options.CurrentValue;
        _logger.LogInformation(
            "[Privacy] Veri saklama taraması {State}. Sohbet={Conv} gün, fotoğraf={Att} gün, aralık={Interval} dk",
            initial.Enabled ? "açık" : "kapalı",
            initial.ConversationRetentionDays, initial.AttachmentRetentionDays, initial.SweepIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Ayar çalışırken değişebilir (IOptionsMonitor) — kapalıyken döngü uyur, açılınca tarar.
            if (_options.CurrentValue.Enabled)
            {
                try { await RunOnceAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { _logger.LogError(ex, "[Privacy] Veri saklama taraması başarısız"); }
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, _options.CurrentValue.SweepIntervalMinutes)), stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        if (_distributedLock is null)
        {
            await _privacy.RunRetentionAsync(DateTime.UtcNow, ct);
            return;
        }

        await using var handle = await _distributedLock.TryAcquireAsync(LockKey, TimeSpan.Zero, ct);
        if (handle is null)
        {
            _logger.LogDebug("[Privacy] Başka bir pod taramada — bu tur atlanıyor.");
            return;
        }

        await _privacy.RunRetentionAsync(DateTime.UtcNow, ct);
    }
}

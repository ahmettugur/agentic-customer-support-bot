// Sesli görüşme arka plan işi: döküm kuyruğunu boşaltır (iş varken beklemeden) ve 5 sn'de bir zaman
// aşımı süpürmesi yapar (çalan → cevapsız, parçası kesilen aktif → başarısız).

using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Services.Voice;

namespace CustomerSupportBot.Api.Workers;

public sealed class VoiceCallWorker(
    VoiceTranscriptionProcessor processor,
    IVoiceCallPort calls,
    ILogger<VoiceCallWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SweepEvery = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastSweep = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTime.UtcNow - lastSweep >= SweepEvery)
                {
                    var closed = await calls.SweepAsync(stoppingToken);
                    if (closed > 0) logger.LogInformation("[VoiceCall] Zaman aşımıyla kapatılan görüşme: {Count}", closed);
                    lastSweep = DateTime.UtcNow;
                }
                if (!await processor.ProcessNextAsync(stoppingToken))
                    await Task.Delay(Idle, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "[VoiceCall] Arka plan işi hatası");
                await Task.Delay(Idle, stoppingToken);
            }
        }
    }
}

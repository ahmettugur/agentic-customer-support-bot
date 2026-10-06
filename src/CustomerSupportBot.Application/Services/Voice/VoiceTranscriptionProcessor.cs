// Kayıt parçalarını sırayla yazıya döker; satırı konuşan etiketiyle yalnız temsilci tarafına yayınlar ve
// konuşma geçmişine ekler (duygu analizi + bota dönüşte bağlam). Çok pod'da sahiplenme depoda.

using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model.Voice;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.Voice;

public sealed class VoiceTranscriptionProcessor(
    IVoiceRecordingStore recordings,
    IVoiceCallStore calls,
    IChatBridge bridge,
    ISessionManager sessions,
    IAudioTranscriber transcriber,
    ILlmSpendGuard budget,
    IOptionsMonitor<VoiceCallOptions> options,
    TimeProvider time,
    ILogger<VoiceTranscriptionProcessor> logger)
{
    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var chunk = await recordings.TryClaimNextPendingAsync(now, ct);
        if (chunk is null) return false;

        if (await budget.CheckAsync(chunk.SessionId, ct) is not null)
        {
            await recordings.PostponeAsync(chunk.Id, now.AddMinutes(1), ct);
            return true;
        }

        var call = await calls.GetAsync(chunk.CallId, ct);
        var speaker = chunk.Track == VoiceTrack.Customer ? "Müşteri" : $"Temsilci ({call?.AgentDisplayName ?? "Temsilci"})";
        var track = VoiceCallService.TrackName(chunk.Track);

        string text;
        try
        {
            text = (await transcriber.TranscribeAsync(chunk.Data, chunk.ContentType, ct)).Trim();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var max = options.CurrentValue.TranscriptionMaxAttempts;
            var final = chunk.Attempts + 1 >= max;
            logger.LogWarning(ex, "[VoiceCall] Döküm başarısız chunk={Chunk} deneme={Attempt}/{Max}", chunk.Id, chunk.Attempts + 1, max);
            await recordings.FailAttemptAsync(chunk.Id, now.AddSeconds(10 * Math.Pow(2, chunk.Attempts)), final, ct);
            if (final)
                await bridge.PublishVoiceTranscriptAsync(chunk.SessionId, chunk.CallId, track, chunk.OffsetMs, $"{speaker}: (döküm alınamadı)");
            return true;
        }

        if (text.Length == 0)
        {
            await recordings.CompleteAsync(chunk.Id, null, ct);
            return true;
        }

        await recordings.CompleteAsync(chunk.Id, text, ct);
        await bridge.PublishVoiceTranscriptAsync(chunk.SessionId, chunk.CallId, track, chunk.OffsetMs, $"{speaker}: {text}");
        if (chunk.Track == VoiceTrack.Customer)
            await sessions.AppendUserMessageAsync(chunk.SessionId, text, ct);
        else
            await sessions.AppendAssistantMessageAsync(chunk.SessionId, text, ct);
        return true;
    }
}

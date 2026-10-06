using CustomerSupportBot.Domain.Model.Voice;

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

/// <summary>Sesli görüşme kayıt parçaları ve dökümleri.</summary>
public interface IVoiceRecordingStore
{
    /// <summary>Aynı (CallId, Track, Sequence) zaten varsa eklemez — ağ tekrarı idempotent.</summary>
    Task<bool> TryAddAsync(VoiceRecordingChunk chunk, CancellationToken ct = default);

    /// <summary>
    /// Sıradaki dökülecek parçayı sahiplenir (Pending → Processing) ve verisiyle döner. 5 dk'dan uzun
    /// Processing kalan (çöken pod) parça yeniden alınabilir. Yoksa <c>null</c>.
    /// </summary>
    Task<VoiceRecordingChunk?> TryClaimNextPendingAsync(DateTime now, CancellationToken ct = default);

    Task CompleteAsync(string chunkId, string? text, CancellationToken ct = default);
    Task FailAttemptAsync(string chunkId, DateTime nextAttemptAt, bool final, CancellationToken ct = default);

    /// <summary>Deneme sayısını artırmadan Pending'e döndürür (ör. LLM bütçesi dolu).</summary>
    Task PostponeAsync(string chunkId, DateTime nextAttemptAt, CancellationToken ct = default);

    /// <summary>Görüşmenin parçaları — ses verisi olmadan, (OffsetMs, Track) sıralı.</summary>
    Task<IReadOnlyList<VoiceRecordingChunk>> ListMetaAsync(string callId, CancellationToken ct = default);

    Task<VoiceRecordingChunk?> GetAsync(string chunkId, CancellationToken ct = default);

    /// <summary>Bu tarihten önce yüklenmiş parçaların sesini siler; döküm metni kalır. Etkilenen sayı.</summary>
    Task<int> PurgeAudioCreatedBeforeAsync(DateTime cutoffUtc, CancellationToken ct = default);
}

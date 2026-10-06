// `voice.recording_chunks` — 10 sn'lik kayıt parçaları ve dökümleri.

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Voice;

public sealed class VoiceRecordingChunkEntity
{
    public string Id { get; set; } = "";
    public string CallId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string Track { get; set; } = "";
    public int Sequence { get; set; }
    public int OffsetMs { get; set; }
    public int DurationMs { get; set; }
    public string ContentType { get; set; } = "";
    public int SizeBytes { get; set; }
    public byte[] Data { get; set; } = [];
    public string TranscriptStatus { get; set; } = "";
    public string? TranscriptText { get; set; }
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public DateTime? AudioPurgedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

// Temsilci tarayıcısının yüklediği 10 sn'lik kayıt parçası (tek başına çözülebilir webm/opus).

namespace CustomerSupportBot.Domain.Model.Voice;

public enum VoiceTrack { Agent, Customer }

public enum VoiceTranscriptStatus { Pending, Processing, Done, Failed }

public sealed class VoiceRecordingChunk
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CallId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public VoiceTrack Track { get; set; }

    /// <summary>İz içindeki sıra (0'dan). (CallId, Track, Sequence) benzersizdir.</summary>
    public int Sequence { get; set; }

    /// <summary>Parçanın görüşme başına göre başlangıcı (ms) — oynatıcıda "o ana atla" için.</summary>
    public int OffsetMs { get; set; }

    public int DurationMs { get; set; }
    public string ContentType { get; set; } = "audio/webm";

    /// <summary>Ses verisi. Listelemelerde ve saklama süresi dolduktan sonra boştur.</summary>
    public byte[] Data { get; set; } = [];

    public VoiceTranscriptStatus TranscriptStatus { get; set; } = VoiceTranscriptStatus.Pending;
    public string? TranscriptText { get; set; }
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime? AudioPurgedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

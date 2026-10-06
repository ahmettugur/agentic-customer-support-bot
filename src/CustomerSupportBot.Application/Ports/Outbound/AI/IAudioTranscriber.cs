namespace CustomerSupportBot.Application.Ports.Outbound.AI;

/// <summary>Tek başına çözülebilir bir ses dosyasını yazıya döker (dil: VoiceCall:TranscriptionLanguage).</summary>
public interface IAudioTranscriber
{
    Task<string> TranscribeAsync(byte[] audio, string contentType, CancellationToken ct = default);
}

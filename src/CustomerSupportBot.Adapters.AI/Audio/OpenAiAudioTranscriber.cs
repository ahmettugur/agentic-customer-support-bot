// Adapters.AI/Audio/OpenAiAudioTranscriber.cs
// IAudioTranscriber — OpenAI ses dökümü (gpt-4o-transcribe / whisper-1). Parça tek başına çözülebilir webm/ogg.

using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using Microsoft.Extensions.Options;
using OpenAI.Audio;

namespace CustomerSupportBot.Adapters.AI.Audio;

/// <summary>
/// İstemci ilk dökümde oluşturulur: arka plan işi açılışta bu adaptörü çözer ve anahtarı olmayan bir ortam
/// (yerel geliştirme, testler) uygulamayı başlangıçta düşürmemeli — döküm o zaman tekrar denenip
/// "(döküm alınamadı)" olarak kapanır, kayıt yine saklanır.
/// </summary>
public sealed class OpenAiAudioTranscriber(Func<AudioClient> clientFactory, IOptionsMonitor<VoiceCallOptions> options)
    : IAudioTranscriber
{
    private readonly Lazy<AudioClient> _client = new(clientFactory);

    public async Task<string> TranscribeAsync(byte[] audio, string contentType, CancellationToken ct = default)
    {
        var extension = contentType.Contains("ogg", StringComparison.OrdinalIgnoreCase) ? "ogg" : "webm";
        using var stream = new MemoryStream(audio);
        var result = await _client.Value.TranscribeAudioAsync(stream, $"chunk.{extension}", new AudioTranscriptionOptions
        {
            Language = options.CurrentValue.TranscriptionLanguage,
            ResponseFormat = AudioTranscriptionFormat.Text
        }, ct);
        return result.Value.Text ?? "";
    }
}

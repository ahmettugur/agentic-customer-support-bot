// Adapters.AI/Realtime/RealtimeTranscriptionConfig.cs
// Sesli oturumda kullanıcı konuşmasının transkripsiyon ayarı — model ailesine göre biçim.

namespace CustomerSupportBot.Adapters.AI.Realtime;

/// <summary>Transkripsiyon modeli ailesi — OpenAI'ye gönderilen ayarın biçimini belirler.</summary>
internal enum TranscriptionModelFamily
{
    /// <summary><c>gpt-4o-transcribe</c>, <c>gpt-4o-mini-transcribe</c>, <c>whisper-1</c> vb.</summary>
    Legacy,

    /// <summary><c>gpt-transcribe</c> — tamamlanmış turu yüksek doğrulukla yazar.</summary>
    Transcribe,

    /// <summary><c>gpt-live-transcribe</c> — konuşurken anlık yazar.</summary>
    LiveTranscribe
}

/// <summary>
/// <c>session.update</c> içindeki <c>audio.input.transcription</c> nesnesini üretir.
///
/// <para>
/// Model appsettings'ten seçilir (<see cref="RealtimeOptions.TranscriptionModel"/>); aileler
/// arasındaki tek fark gönderilen alanlardır:
/// </para>
/// <list type="table">
/// <item><term>Legacy</term><description><c>language</c> (tek değer) + <c>prompt</c></description></item>
/// <item><term>Transcribe</term><description><c>languages</c> (liste) + <c>prompt</c> + <c>keywords</c></description></item>
/// <item><term>LiveTranscribe</term><description>Transcribe alanları + <c>delay</c></description></item>
/// </list>
/// <para>
/// Yeni nesil modellere <c>language</c> gönderilmez — sağlayıcı <c>language</c> ile <c>languages</c>'ın
/// birlikte gönderilmesini reddeder. Bir modele uymayan ayar (ör. eski modelde
/// <c>TranscriptionDelay</c>) gönderilmez ve uyarı olarak döner; çağıran loglar — ayar sessizce
/// etkisiz kalmasın.
/// </para>
/// </summary>
internal static class RealtimeTranscriptionConfig
{
    /// <summary><c>gpt-live-transcribe</c>'ın kabul ettiği <c>delay</c> değerleri.</summary>
    internal static readonly IReadOnlyList<string> DelayValues = ["minimal", "low", "medium", "high", "xhigh"];

    /// <summary>
    /// Model adının başından aileyi çıkarır; tarihli sürümler (<c>gpt-transcribe-2026-07-29</c>)
    /// ve büyük/küçük harf farkı tanınır. Bilinmeyen adlar önceki nesil biçimine düşer — bugünkü
    /// varsayılan (<c>gpt-4o-transcribe</c>) ile davranış birebir aynı kalır.
    /// </summary>
    public static TranscriptionModelFamily Classify(string? model)
    {
        var name = model?.Trim() ?? "";
        if (name.StartsWith("gpt-live-transcribe", StringComparison.OrdinalIgnoreCase))
            return TranscriptionModelFamily.LiveTranscribe;
        if (name.StartsWith("gpt-transcribe", StringComparison.OrdinalIgnoreCase))
            return TranscriptionModelFamily.Transcribe;
        return TranscriptionModelFamily.Legacy;
    }

    /// <summary>Transkripsiyon ayarını ve uygulanamayan ayarlar için uyarıları üretir.</summary>
    public static (Dictionary<string, object?> Config, IReadOnlyList<string> Warnings) Build(RealtimeOptions options)
    {
        var model = options.TranscriptionModel?.Trim() ?? "";
        var family = Classify(model);
        var warnings = new List<string>();
        var config = new Dictionary<string, object?> { ["model"] = model };

        var language = options.TranscriptionLanguage?.Trim();
        if (!string.IsNullOrEmpty(language))
        {
            if (family == TranscriptionModelFamily.Legacy) config["language"] = language;
            else config["languages"] = new[] { language };
        }

        if (!string.IsNullOrWhiteSpace(options.TranscriptionPrompt))
            config["prompt"] = options.TranscriptionPrompt;

        var keywords = (options.TranscriptionKeywords ?? [])
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (keywords.Length > 0)
        {
            if (family == TranscriptionModelFamily.Legacy)
                warnings.Add($"TranscriptionKeywords yalnızca gpt-transcribe / gpt-live-transcribe ile gönderilir; '{model}' için yok sayıldı.");
            else
                config["keywords"] = keywords;
        }

        var delay = options.TranscriptionDelay?.Trim();
        if (!string.IsNullOrEmpty(delay))
        {
            if (family != TranscriptionModelFamily.LiveTranscribe)
            {
                warnings.Add($"TranscriptionDelay yalnızca gpt-live-transcribe ile gönderilir; '{model}' için yok sayıldı.");
            }
            else if (!DelayValues.Contains(delay, StringComparer.OrdinalIgnoreCase))
            {
                warnings.Add($"TranscriptionDelay '{delay}' geçersiz (geçerli: {string.Join(", ", DelayValues)}); " +
                             "sağlayıcı varsayılanı kullanılacak.");
            }
            else
            {
                config["delay"] = delay.ToLowerInvariant();
            }
        }

        return (config, warnings);
    }
}

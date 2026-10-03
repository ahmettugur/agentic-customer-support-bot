// Adapters.AI/Options/AiProviderOptions.cs
// AI provider configuration — strongly-typed options bound from appsettings.json > "AI".
// Lives in the AI adapter layer; Domain has no dependency on infrastructure settings.

namespace CustomerSupportBot.Adapters.AI;

/// <summary>Active AI provider selection.</summary>
public enum AiProvider
{
    OpenAI,
    AzureOpenAI
}

/// <summary>
/// AI provider root configuration — bound from appsettings.json > "AI".
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "AI";

    public AiProvider Provider { get; set; }

    public OpenAiOptions OpenAI { get; set; } = new();
    public AzureOpenAiOptions AzureOpenAI { get; set; } = new();
    public RealtimeOptions Realtime { get; set; } = new();
}

/// <summary>
/// OpenAI Realtime API (voice) settings.
/// Used for wss://api.openai.com/v1/realtime?model={Model}.
/// Falls back to <see cref="OpenAiOptions.ApiKey"/> when ApiKey is empty.
/// </summary>
public sealed class RealtimeOptions
{
    public bool Enabled { get; set; } = true;
    public string Model { get; set; } = "gpt-realtime-2";
    public string? ApiKey { get; set; }
    public string Voice { get; set; } = null!;
    public int VadSilenceMs { get; set; } = 600;
    public string ReasoningEffort { get; set; } = "low";
    public int MaxResponseTokens { get; set; } = 4096;
    /// <summary>
    /// Kullanıcı konuşmasının transkripsiyon modeli. Desteklenen aileler (bkz.
    /// <c>RealtimeTranscriptionConfig</c>):
    /// <list type="bullet">
    /// <item><c>gpt-4o-transcribe</c> (varsayılan), <c>gpt-4o-mini-transcribe</c>, <c>whisper-1</c> —
    /// önceki nesil; dil tek değer olarak (<c>language</c>) gönderilir.</item>
    /// <item><c>gpt-transcribe</c> — tamamlanmış turu yüksek doğrulukla yazar; dil listesi
    /// (<c>languages</c>) ve isteğe bağlı <see cref="TranscriptionKeywords"/> gönderilir.</item>
    /// <item><c>gpt-live-transcribe</c> — konuşurken anlık yazar; ek olarak
    /// <see cref="TranscriptionDelay"/> gönderilir.</item>
    /// </list>
    /// Aile model adının başından anlaşılır; tarihli sürümler (ör. <c>gpt-transcribe-2026-07-29</c>)
    /// da tanınır.
    /// </summary>
    public string TranscriptionModel { get; set; } = "gpt-4o-transcribe";
    public string? TranscriptionLanguage { get; set; } = "tr";

    /// <summary>
    /// Ses içinde geçmesi beklenen terimler (ör. ürün/kategori adları, "iade", "kargo").
    /// Yalnızca yeni nesil modellerde (<c>gpt-transcribe</c>, <c>gpt-live-transcribe</c>) gönderilir.
    /// <b>Örnek numara/cümle YAZMAYIN</b> — <see cref="TranscriptionPrompt"/>'taki aynı sebeple:
    /// model sessizlikte bu sözlükten metin uydurabilir.
    /// </summary>
    public List<string> TranscriptionKeywords { get; set; } = new();

    /// <summary>
    /// Yalnızca <c>gpt-live-transcribe</c>: anlık metin üretiminde gecikme/doğruluk dengesi —
    /// <c>minimal</c>, <c>low</c>, <c>medium</c>, <c>high</c>, <c>xhigh</c>. Daha yüksek değer modele
    /// daha fazla ses bağlamı verir, hata oranını düşürür. Boşsa sağlayıcı varsayılanı kullanılır.
    /// </summary>
    public string? TranscriptionDelay { get; set; }
    /// <summary>
    /// ASR'ye verilen alan ipucu. <b>Örnek numara/cümle YAZMAYIN.</b>
    ///
    /// <para>
    /// Bu prompt eskiden somut örnekler içeriyordu ("müşteri numarası (1008, 1027 gibi...)").
    /// Transkripsiyon modeli sessizlik veya gürültüde boş dönmek yerine prompt'un sözlüğünden
    /// olası bir cümle uyduruyor; örnekler verildiğinde bu halüsinasyon domain'e birebir
    /// benzeyen, gerçek gibi görünen bir cümleye dönüşüyordu. Canlıda kullanıcı hiçbir şey
    /// söylemeden sohbete "Merhaba, müşteri numaram 1025." düştü (prompt'taki 1008/1027
    /// örneklerinin komşusu bir sayı) ve bu sahte metin bir agent turu başlattı.
    /// Alan/dil ipucu bırakıldı, tohumlayıcı örnekler kaldırıldı.
    /// </para>
    /// </summary>
    public string TranscriptionPrompt { get; set; } =
        "Müşteri destek görüşmesi. Sipariş, kargo, iade, şikayet ve ürün konuları geçer. " +
        "Sipariş ve müşteri numaraları rakamla söylenir. Türkçe konuşulur.";
}

/// <summary>OpenAI public API settings.</summary>
public sealed class OpenAiOptions
{
    public string? ApiKey { get; set; }
    public string? Model { get; set; }
    public string? ReasoningModel { get; set; }
    public string? ReasoningEffort { get; set; }
}

/// <summary>Azure OpenAI settings (deployment-based).</summary>
public sealed class AzureOpenAiOptions
{
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public string? Deployment { get; set; }
    public string? ReasoningDeployment { get; set; }
    public string? ReasoningEffort { get; set; }
}

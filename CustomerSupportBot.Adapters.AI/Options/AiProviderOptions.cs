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
    public string TranscriptionModel { get; set; } = "gpt-4o-transcribe";
    public string? TranscriptionLanguage { get; set; } = "tr";
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

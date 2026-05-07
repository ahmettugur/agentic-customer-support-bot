// Models/AiProviderOptions.cs
// AI sağlayıcı yapılandırmaları için strongly-typed options sınıfları.
// `appsettings.json` > "AI" bölümünden IOptions<T> ile bind edilir.

namespace CustomerSupportBot.Models;

/// <summary>Aktif AI sağlayıcısı.</summary>
public enum AiProvider
{
    OpenAI,
    AzureOpenAI,
    Anthropic
}

/// <summary>
/// AI sağlayıcı kök yapılandırması — `appsettings.json` > "AI" bölümüne bind edilir.
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "AI";

    /// <summary>Aktif sağlayıcı (appsettings'ten gelir).</summary>
    public AiProvider Provider { get; set; }

    public OpenAiOptions OpenAI { get; set; } = new();
    public AzureOpenAiOptions AzureOpenAI { get; set; } = new();
    public AnthropicOptions Anthropic { get; set; } = new();
    public RealtimeOptions Realtime { get; set; } = new();
}

/// <summary>
/// OpenAI Realtime API (sesli konuşma) ayarları.
/// `wss://api.openai.com/v1/realtime?model={Model}` endpoint'i için kullanılır.
/// API key boşsa <see cref="OpenAiOptions.ApiKey"/> kullanılır.
/// </summary>
public sealed class RealtimeOptions
{
    /// <summary>Realtime özelliğini aç/kapa.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Realtime model adı (ör. "gpt-realtime-1.5").</summary>
    public string Model { get; set; } = "gpt-realtime-1.5";

    /// <summary>Override API key — boşsa <see cref="OpenAiOptions.ApiKey"/> kullanılır.</summary>
    public string? ApiKey { get; set; }

    /// <summary>TTS sesi (alloy, ash, ballad, coral, echo, sage, shimmer, verse).</summary>
    public string Voice { get; set; } = "alloy";

    /// <summary>Server-side VAD susturma süresi (ms) — kullanıcının konuşmayı bitirdiğine karar verme eşiği.</summary>
    public int VadSilenceMs { get; set; } = 600;

    /// <summary>Server-side VAD eşiği (0..1). Yüksek = daha az hassas (gürültülü ortamda artır).</summary>
    public double VadThreshold { get; set; } = 0.5;

    /// <summary>Asistan yanıtının en çok kaç token söylenebileceği (TTS sırasında).</summary>
    public int MaxResponseTokens { get; set; } = 4096;
}

/// <summary>OpenAI public API ayarları.</summary>
public sealed class OpenAiOptions
{
    public string? ApiKey { get; set; }
    public string? Model { get; set; }
    public string? ReasoningModel { get; set; }
    public string? ReasoningEffort { get; set; }
}

/// <summary>Azure OpenAI ayarları (deployment-tabanlı).</summary>
public sealed class AzureOpenAiOptions
{
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    /// <summary>Azure'da yayınlanmış deployment adı (model değil).</summary>
    public string? Deployment { get; set; }
    public string? ReasoningDeployment { get; set; }
    public string? ReasoningEffort { get; set; }
}

/// <summary>Anthropic Claude ayarları.</summary>
public sealed class AnthropicOptions
{
    public string? ApiKey { get; set; }
    public string? Model { get; set; }
    public string? ReasoningModel { get; set; }
    /// <summary>Çıktı token üst sınırı (Anthropic zorunlu kılıyor).</summary>
    public int MaxTokens { get; set; }
}

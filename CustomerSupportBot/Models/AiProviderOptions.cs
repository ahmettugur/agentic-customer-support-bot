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

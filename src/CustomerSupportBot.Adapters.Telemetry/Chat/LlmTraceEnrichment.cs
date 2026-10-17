// Adapters.Telemetry/Chat/LlmTraceEnrichment.cs
// LLM span'ine Langfuse için eklenen çağrı anı bağlamı: kullanıcı kimliği ve (isteğe bağlı) prompt/yanıt içeriği.

namespace CustomerSupportBot.Adapters.Telemetry.Chat;

/// <summary>
/// <see cref="TelemetryChatClient"/>'ın LLM span'ini zenginleştirme ayarları. Kompozisyon kökünde
/// (<c>AiServicesExtensions</c>) <c>Telemetry:CaptureLlmContent</c> ve uygulamanın PII maskeleyicisiyle kurulur.
/// </summary>
public sealed class LlmTraceEnrichment
{
    /// <summary>
    /// Prompt ve yanıt metni span'e yazılsın mı (<c>langfuse.observation.input/output</c>). Varsayılan KAPALI:
    /// metin müşteri verisi taşır ve trace altyapısına (Jaeger/Langfuse) gider.
    /// </summary>
    public bool CaptureContent { get; init; }

    /// <summary>Girdi ve çıktı için ayrı ayrı üst sınır (karakter); aşan metin kısaltılır.</summary>
    public int MaxContentChars { get; init; } = 8000;

    /// <summary>Span'e yazılmadan önce her metne uygulanan PII maskeleyici.</summary>
    public Func<string, string> Redact { get; init; } = static s => s;

    /// <summary>Çağrı anındaki müşteri kimliği (Langfuse "Users" görünümü); yoksa null.</summary>
    public Func<string?> CurrentUserId { get; init; } = static () => null;
}

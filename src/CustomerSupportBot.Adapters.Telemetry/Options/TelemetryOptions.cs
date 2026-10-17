// Adapters.Telemetry/Options/TelemetryOptions.cs
// OpenTelemetry pipeline configuration — bound from appsettings.json > "Telemetry".
// Lives in the Telemetry adapter layer; Domain has no dependency on infrastructure settings.

namespace CustomerSupportBot.Adapters.Telemetry;

public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    public bool Enabled { get; set; } = true;
    public string ServiceName { get; set; } = "CustomerSupportBot.Api";
    public string ServiceVersion { get; set; } = "1.0.0";
    public OtlpExporterOptions Otlp { get; set; } = new();
    public bool TracingEnabled { get; set; } = true;
    public bool MetricsEnabled { get; set; } = true;

    /// <summary>
    /// LLM prompt ve yanıt metni trace'e yazılsın mı (Langfuse'ta generation girdi/çıktısı). Varsayılan KAPALI:
    /// metin müşteri verisi taşır. Açıkken kart/telefon/TC/e-posta maskelenir (<c>InputGuard.RedactPii</c>);
    /// ad-soyad gibi serbest kişisel veri maskelenmez. Ayrıntı: docs/langfuse.md
    /// </summary>
    public bool CaptureLlmContent { get; set; }

    /// <summary>İçerik kaydında girdi ve çıktı için ayrı ayrı üst sınır (karakter).</summary>
    public int MaxLlmContentChars { get; set; } = 8000;

    /// <summary>
    /// Per-model USD/1K token pricing table. Key = model name (case-insensitive).
    /// "default" key is used as fallback for unknown models.
    /// </summary>
    public Dictionary<string, ModelPricing> Pricing { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public sealed class OtlpExporterOptions
    {
        public string? Endpoint { get; set; }
        public string Protocol { get; set; } = "grpc";
        public string? Headers { get; set; }
    }

    public sealed class ModelPricing
    {
        public decimal InputPer1K { get; set; }
        public decimal OutputPer1K { get; set; }
    }
}

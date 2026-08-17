// Adapters.Telemetry/Models/TelemetryAdapterOptions.cs
// Telemetri adapter yapılandırma modeli.

namespace CustomerSupportBot.Adapters.Telemetry.Models;

/// <summary>
/// Telemetri adaptörü için yapılandırma seçenekleri.
/// </summary>
public class TelemetryAdapterOptions
{
    public const string SectionName = "Telemetry";

    /// <summary>Telemetri aktif mi?</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Tracing aktif mi?</summary>
    public bool TracingEnabled { get; set; } = true;

    /// <summary>Metrics aktif mi?</summary>
    public bool MetricsEnabled { get; set; } = true;

    /// <summary>Service adı (OTLP'de görünür).</summary>
    public string ServiceName { get; set; } = "CustomerSupportBot";

    /// <summary>Service versiyonu.</summary>
    public string ServiceVersion { get; set; } = "1.0.0";

    /// <summary>OTLP exporter ayarları.</summary>
    public OtlpExporterOptions Otlp { get; set; } = new();

    /// <summary>Model fiyat tablosu (USD / 1K token).</summary>
    public Dictionary<string, ModelPricing> Pricing { get; set; } = new();

    public class OtlpExporterOptions
    {
        /// <summary>OTLP endpoint (örn: http://localhost:4317).</summary>
        public string? Endpoint { get; set; }

        /// <summary>Protokol: "grpc" veya "httpprotobuf".</summary>
        public string? Protocol { get; set; }

        /// <summary>Ek başlıklar (Authorization vb.).</summary>
        public string? Headers { get; set; }
    }

    public class ModelPricing
    {
        /// <summary>1K input token başına USD maliyeti.</summary>
        public decimal InputPer1K { get; set; }

        /// <summary>1K output token başına USD maliyeti.</summary>
        public decimal OutputPer1K { get; set; }
    }
}

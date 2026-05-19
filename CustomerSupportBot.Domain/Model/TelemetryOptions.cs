// Domain/Model/TelemetryOptions.cs
// appsettings.json > "Telemetry" bölümüne bind edilen opsiyonlar.
// OpenTelemetry trace + metric pipeline'ı, OTLP exporter ve model bazlı
// USD maliyet tablosu burada yapılandırılır.

namespace CustomerSupportBot.Domain.Model;

public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    /// <summary>Tüm telemetri pipeline'ı kapatılabilir (varsayılan açık).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>OpenTelemetry resource service.name attribute değeri.</summary>
    public string ServiceName { get; set; } = "CustomerSupportBot.Api";

    /// <summary>OpenTelemetry resource service.version attribute değeri.</summary>
    public string ServiceVersion { get; set; } = "1.0.0";

    /// <summary>OTLP exporter ayarları. Endpoint boşsa exporter eklenmez (sadece in-process).</summary>
    public OtlpExporterOptions Otlp { get; set; } = new();

    /// <summary>Tracing alt sistemi (span üretimi).</summary>
    public bool TracingEnabled { get; set; } = true;

    /// <summary>Metric alt sistemi (counter / histogram).</summary>
    public bool MetricsEnabled { get; set; } = true;

    /// <summary>
    /// Model bazlı USD/1K token fiyat tablosu. Anahtar = model adı (case-insensitive eşleşir).
    /// "default" anahtarı bulunmayan modeller için fallback olarak kullanılır.
    /// </summary>
    public Dictionary<string, ModelPricing> Pricing { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public sealed class OtlpExporterOptions
    {
        /// <summary>OTLP collector endpoint (örn. http://localhost:4317 — gRPC).</summary>
        public string? Endpoint { get; set; }

        /// <summary>"grpc" veya "httpprotobuf". Default = grpc.</summary>
        public string Protocol { get; set; } = "grpc";

        /// <summary>Ek header'lar (key=value;key=value).</summary>
        public string? Headers { get; set; }
    }

    public sealed class ModelPricing
    {
        /// <summary>Input (prompt) token başına USD — 1.000 token üzerinden.</summary>
        public decimal InputPer1K { get; set; }

        /// <summary>Output (completion) token başına USD — 1.000 token üzerinden.</summary>
        public decimal OutputPer1K { get; set; }
    }
}

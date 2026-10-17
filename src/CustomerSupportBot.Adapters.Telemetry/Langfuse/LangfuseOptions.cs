// Adapters.Telemetry/Langfuse/LangfuseOptions.cs
// Langfuse public API erişimi (skor gönderme) — Telemetry:Langfuse bölümünden bağlanır.
// Trace'ler buradan DEĞİL, OTel Collector üzerinden gider; bu ayarlar yalnızca REST API içindir.

namespace CustomerSupportBot.Adapters.Telemetry.Langfuse;

public sealed class LangfuseOptions
{
    public const string SectionName = "Telemetry:Langfuse";

    /// <summary>Kapalıyken müşteri puanı Langfuse'a gönderilmez.</summary>
    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "http://localhost:3000";

    /// <summary>Proje public key (pk-lf-...). Gizli değer — user-secrets/ortam değişkeni.</summary>
    public string PublicKey { get; set; } = "";

    /// <summary>Proje secret key (sk-lf-...). Gizli değer — user-secrets/ortam değişkeni.</summary>
    public string SecretKey { get; set; } = "";
}

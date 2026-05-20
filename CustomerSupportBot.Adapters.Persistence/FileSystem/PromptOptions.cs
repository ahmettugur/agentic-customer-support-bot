// Adapters.Persistence/FileSystem/PromptOptions.cs
// Prompt repository yapılandırma seçenekleri.

namespace CustomerSupportBot.Adapters.Persistence.FileSystem;

/// <summary>
/// Prompt şablon yükleme yapılandırması.
/// appsettings.json veya env variable ile ayarlanabilir.
/// </summary>
public sealed class PromptOptions
{
    public const string SectionName = "Prompts";

    /// <summary>
    /// Prompt dosyalarının bulunduğu dizin yolu.
    /// Göreli yol verilirse AppContext.BaseDirectory'e göre çözümlenir.
    /// Azure Files, NFS veya cloud-mounted volume yolu verilebilir.
    /// Boş bırakılırsa varsayılan "Prompts" dizini kullanılır.
    /// </summary>
    public string? RootPath { get; set; }

    /// <summary>
    /// true ise başlangıçta prompt bulunamazsa hata fırlatmak yerine
    /// boş dictionary ile başlar. Opsiyonel lazy loading senaryoları için.
    /// </summary>
    public bool AllowEmpty { get; set; }
}

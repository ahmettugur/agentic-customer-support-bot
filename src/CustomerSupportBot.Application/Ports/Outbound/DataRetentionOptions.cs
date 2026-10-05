// Ports/Outbound/DataRetentionOptions.cs
// Kişisel veri saklama süreleri (KVKK) — appsettings "DataRetention" bölümü.

namespace CustomerSupportBot.Application.Ports.Outbound;

/// <summary>
/// Otomatik veri temizliği. Kapalıyken (<see cref="Enabled"/> = false) hiçbir şey silinmez; müşterinin
/// kendi verisini indirmesi ve silmesi bu ayardan bağımsız olarak her zaman açıktır.
/// </summary>
public sealed class DataRetentionOptions
{
    public const string SectionName = "DataRetention";

    public bool Enabled { get; set; } = true;

    /// <summary>Son etkinliği bundan eski oturumlar (mesajları, fotoğrafları ve bağlı kayıtlarıyla) silinir.</summary>
    public int ConversationRetentionDays { get; set; } = 180;

    /// <summary>Bundan eski fotoğraflar silinir — oturum daha yeni olsa bile (veri en aza indirme).</summary>
    public int AttachmentRetentionDays { get; set; } = 90;

    /// <summary>Tarama aralığı.</summary>
    public int SweepIntervalMinutes { get; set; } = 60;

    /// <summary>Bir taramada silinecek en fazla oturum; kalanlar sonraki taramaya kalır (yükü yaymak için).</summary>
    public int MaxSessionsPerSweep { get; set; } = 500;
}

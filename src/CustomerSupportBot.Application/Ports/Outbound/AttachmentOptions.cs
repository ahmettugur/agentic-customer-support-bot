// Application/Ports/Outbound/AttachmentOptions.cs
// Sohbete eklenen fotoğrafların sınırları — appsettings.json > "Attachments".

namespace CustomerSupportBot.Application.Ports.Outbound;

public sealed class AttachmentOptions
{
    public const string SectionName = "Attachments";

    /// <summary>Fotoğraf ekleme açık mı?</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Tek fotoğraf için en büyük boyut (bayt).</summary>
    public int MaxBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>Bir mesaja eklenebilecek en fazla fotoğraf.</summary>
    public int MaxPerMessage { get; set; } = 3;

    /// <summary>Bir oturumda toplam en fazla fotoğraf.</summary>
    public int MaxPerSession { get; set; } = 10;
}

// Domain/Model/ChatAttachment.cs
// Müşterinin yazılı sohbette eklediği fotoğraf.

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// Müşterinin sohbette eklediği fotoğraf (JPEG/PNG; meta verisi silinmiş). Görsel modelin ürettiği
/// kısa açıklama ajanlara metin olarak verilir; fotoğraf bir onay kaydına bağlanınca temsilci onay
/// kartında görür. Oturum silinince fotoğraf da silinir.
/// </summary>
public sealed class ChatAttachment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionId { get; set; } = "";
    public string? CustomerId { get; set; }

    /// <summary><c>image/jpeg</c> veya <c>image/png</c> — dosya imzasından belirlenir.</summary>
    public string ContentType { get; set; } = "";

    public byte[] Data { get; set; } = [];

    /// <summary>Görsel modelin açıklaması (kişisel veri maskelenmiş). Üretilemediyse <c>null</c>.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Bir mesajla gönderildiği an. Yüklenip gönderilmeyen (önizlemeden kaldırılan, sekme kapatılan)
    /// fotoğraf <c>null</c> kalır ve hiçbir onaya bağlanmaz.
    /// </summary>
    public DateTime? SentAt { get; set; }

    /// <summary>Bağlandığı onay kaydı — bağlanmamışsa <c>null</c>.</summary>
    public string? ApprovalId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

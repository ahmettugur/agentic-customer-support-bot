// Models/MessageFeedback.cs
// Müşterinin tek bir bot yanıtına verdiği 👍/👎 geri bildirimi (konuşma sonu yıldız puanından ayrıdır).

using System.Security.Cryptography;
using System.Text;

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// Bir bot yanıtına geri bildirim. Yanıt, <b>(oturum, metin özeti)</b> ile tanınır: konuşma geçmişi mesaj kimliği ya da
/// trace kimliği taşımaz, ama geçmişe yazılan metin ile trace'in <c>FinalResponse</c>'u aynı kanonik metindir. Özet
/// sunucuda hesaplanır ve bir müşteri bir mesaja tek geri bildirim verebilir (yeniden verince güncellenir).
/// </summary>
public sealed class MessageFeedback
{
    public const int MaxCommentLength = 300;

    public string SessionId { get; set; } = "";

    /// <summary><see cref="HashOf"/> ile üretilen mesaj özeti.</summary>
    public string MessageHash { get; set; } = "";

    public bool Helpful { get; set; }

    /// <summary>Yalnızca 👎'de, <see cref="MessageFeedbackReasons"/> değerlerinden biri.</summary>
    public string? Reason { get; set; }

    /// <summary>Serbest metin (kişisel veri içerebilir): KVKK silme/dışa aktarma kapsamındadır.</summary>
    public string? Comment { get; set; }

    /// <summary>Yanıtı üreten tur, bulunabildiyse (öğrenme ve inceleme için).</summary>
    public string? TraceId { get; set; }

    public string? CustomerId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Mesaj özeti: satır sonu farkı ve baştaki/sondaki boşluk sayılmaz; SHA-256'nın ilk 16 hex karakteri.</summary>
    public static string HashOf(string text)
    {
        var canonical = text.Replace("\r\n", "\n").Trim();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16].ToLowerInvariant();
    }
}

/// <summary>👎 için sabit neden kodları: serbest metin yerine ölçülebilir kategoriler.</summary>
public static class MessageFeedbackReasons
{
    public const string Wrong = "wrong";            // Yanlış bilgi
    public const string Irrelevant = "irrelevant";  // Sorumu yanıtlamadı
    public const string Unclear = "unclear";        // Anlaşılmadı
    public const string Other = "other";

    public static readonly IReadOnlyList<string> All = [Wrong, Irrelevant, Unclear, Other];

    public static bool IsValid(string? reason) => reason is not null && All.Contains(reason, StringComparer.Ordinal);
}

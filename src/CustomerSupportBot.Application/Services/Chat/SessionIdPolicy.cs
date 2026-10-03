// Application/Services/Chat/SessionIdPolicy.cs
// İstemciden gelen sessionId'nin kabul edilebilir biçimi — tek kaynak.

namespace CustomerSupportBot.Application.Services.Chat;

/// <summary>
/// İstemcinin gönderdiği <c>sessionId</c> için biçim kuralı.
///
/// <para>
/// <c>sessionId</c> gövde veya URL'den gelir ve doğrudan <c>chat.sessions.session_id</c>
/// (varchar(64)) anahtarı olur. Doğrulama yokken 64 karakterden uzun bir id önce süreç içi
/// cache'e ekleniyor, ardından DB yazması patlıyordu; keyfi karakterler (boşluk, yol ayraçları,
/// HTML) de log ve panel yüzeylerine taşınıyordu. Sunucunun kendi ürettiği biçim (<c>Guid</c>,
/// 36 karakter) her zaman geçerlidir.
/// </para>
/// </summary>
public static class SessionIdPolicy
{
    /// <summary>DB kolon sınırı — <c>SessionConfiguration</c> ile aynı tutulmalı.</summary>
    public const int MaxLength = 64;

    /// <summary>Hata yanıtlarında kullanılan makine-okunur kod.</summary>
    public const string ErrorCode = "invalid_session_id";

    public const string ErrorMessage = "Geçersiz oturum kimliği.";

    /// <summary>1–64 karakter; yalnızca ASCII harf, rakam, '-' ve '_'.</summary>
    public static bool IsValid(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId) || sessionId.Length > MaxLength) return false;

        foreach (var c in sessionId)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) return false;
        }
        return true;
    }
}

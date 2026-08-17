using System.Text.Json;

namespace CustomerSupportBot.Web.Services;

/// <summary>
/// JWT payload'ından `exp` claim'ini imza doğrulaması yapmadan okur — WASM tarafında
/// "bu token'ın süresi geçmiş mi, sunucuya sormadan önce bakayım" sorusuna yeterlidir.
/// Gerçek güvenlik doğrulaması zaten sunucuda yapılıyor; burası yalnızca UX içindir
/// (stale token'la sayfaya girip 401'e çarpmak yerine baştan login'e yönlendirmek).
/// </summary>
public static class JwtUtils
{
    public static DateTimeOffset? TryGetExpiryUtc(string? jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt)) return null;

        var parts = jwt.Split('.');
        if (parts.Length < 2) return null;

        try
        {
            var payloadJson = System.Text.Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("exp", out var expEl) && expEl.TryGetInt64(out var expSeconds))
                return DateTimeOffset.FromUnixTimeSeconds(expSeconds);
        }
        catch
        {
            // Bozuk/beklenmedik formatlı token — çağıran taraf bunu "bilinmiyor" olarak
            // ele alıp normal akışa (sunucunun 401 ile reddetmesine) bırakmalı.
        }

        return null;
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        s = (s.Length % 4) switch
        {
            2 => s + "==",
            3 => s + "=",
            _ => s
        };
        return Convert.FromBase64String(s);
    }
}

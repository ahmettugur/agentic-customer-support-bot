// Ports/Driven/Auth/JwtOptions.cs\n// JWT altyapı yapılandırması — Application katmanında tanımlıdır çünkü\n// hem Adapters.Persistence (TokenService) hem de API (AuthServicesExtensions) tarafından kullanılır.\n\nnamespace CustomerSupportBot.Application.Ports.Outbound.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "CustomerSupportBot.Api";
    public string Audience { get; set; } = "CustomerSupportBot.Api";

    /// <summary>HMAC-SHA256 signing key (UTF-8). Üretimde rotate edilmeli.</summary>
    public string SigningKey { get; set; } = "";

    /// <summary>Access token ömrü (dakika).</summary>
    public int AccessTokenMinutes { get; set; } = 30;

    /// <summary>Refresh token ömrü (gün).</summary>
    public int RefreshTokenDays { get; set; } = 14;

    /// <summary>
    /// /auth/* uçlarının (login, customer/login, customer/register, refresh) IP başına
    /// dakikalık hız sınırı. Bu uçlar kimliksizdir (AllowAnonymous) — A2A'daki gibi partner
    /// claim'i yoktur, tek ayırt edici çağıranın IP'sidir.
    /// </summary>
    public int AuthRateLimitPerMinute { get; set; } = 10;

    /// <summary>HMAC-SHA256 için asgari anahtar uzunluğu (bayt, UTF-8).</summary>
    public const int MinSigningKeyBytes = 32;

    /// <summary>
    /// Asgari farklı karakter sayısı. Gerçek bir entropi ölçümü değildir; "aaaa…" ya da
    /// "abcabc…" gibi uzunluk kuralını geçip tahmin edilebilir kalan anahtarları reddeder.
    /// Rastgele üretilmiş (ör. <c>openssl rand -base64 48</c>) bir anahtar bunu rahatça aşar.
    /// </summary>
    public const int MinSigningKeyDistinctChars = 12;

    /// <summary>
    /// İmzalama anahtarını doğrular; geçersizse nedenini, geçerliyse <c>null</c> döner.
    /// Token ÜRETEN (JwtAccessTokenProvider) ve DOĞRULAYAN (JwtBearer) taraf aynı kuralı
    /// kullanır — biri anahtarı reddederken diğerinin sessizce kabul etmesi mümkün olmasın.
    /// </summary>
    public static string? ValidateSigningKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return "Jwt:SigningKey tanımlı değil.";
        if (System.Text.Encoding.UTF8.GetByteCount(key) < MinSigningKeyBytes)
            return $"Jwt:SigningKey en az {MinSigningKeyBytes} bayt olmalı (HMAC-SHA256).";
        if (key.Distinct().Count() < MinSigningKeyDistinctChars)
            return $"Jwt:SigningKey çok zayıf (en az {MinSigningKeyDistinctChars} farklı karakter gerekir). "
                 + "Rastgele üretilmiş bir anahtar kullanın.";
        return null;
    }
}


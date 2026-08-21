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
}


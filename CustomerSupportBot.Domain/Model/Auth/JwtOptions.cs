// Models/Auth/AuthOptions.cs

namespace CustomerSupportBot.Domain.Model.Auth;

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
}


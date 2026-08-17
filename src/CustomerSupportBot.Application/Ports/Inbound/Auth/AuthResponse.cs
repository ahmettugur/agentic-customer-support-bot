// Ports/Driving/Auth/AuthResponse.cs
// ITokenService driving port'unun use case output DTO'su.

namespace CustomerSupportBot.Application.Ports.Inbound.Auth;

/// <summary>
/// Authentication yanıtı — use case boundary output.
/// </summary>
/// <param name="Username">Giriş kimliği — müşteri hesaplarında e-posta.</param>
/// <param name="FullName">
/// Müşterinin katalogdaki adı soyadı (yalnızca Customer rolünde dolu; staff hesaplarında null).
/// Arayüzün kullanıcıya adıyla hitap edebilmesi için döner — <see cref="Username"/> e-posta
/// olduğundan tek başına gösterime uygun değil. Kullanıcının KENDİ verisi olduğu için ek bir
/// yetkilendirme gerektirmez; başka müşterinin adı bu yolla asla dönmez (kimlik JWT'nin
/// bağlı olduğu hesaptan çözülür).
/// </param>
public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAt,
    DateTime RefreshTokenExpiresAt,
    string Username,
    string Role,
    string? LinkedAgentId = null,
    string? FullName = null);

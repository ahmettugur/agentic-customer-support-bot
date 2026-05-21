// Ports/Driving/Auth/AuthResponse.cs
// ITokenService driving port'unun use case output DTO'su.

namespace CustomerSupportBot.Application.Ports.Driving.Auth;

/// <summary>
/// Authentication yanıtı — use case boundary output.
/// </summary>
public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAt,
    DateTime RefreshTokenExpiresAt,
    string Username,
    string Role,
    string? LinkedAgentId = null);

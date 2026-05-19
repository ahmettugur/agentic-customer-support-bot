namespace CustomerSupportBot.Domain.Model.Auth;

public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAt,
    DateTime RefreshTokenExpiresAt,
    string Username,
    string Role,
    string? LinkedAgentId = null);

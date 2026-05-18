// Models/Auth/AuthDtos.cs
// Login + refresh için DTO'lar.

namespace CustomerSupportBot.Api.Models.Auth;

public sealed record LoginRequest(string Username, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAt,
    DateTime RefreshTokenExpiresAt,
    string Username,
    string Role,
    string? LinkedAgentId = null);


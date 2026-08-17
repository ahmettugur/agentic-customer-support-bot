// Domain/Model/Auth/RefreshTokenInfo.cs
// Refresh token snapshot — adapter-agnostic token record.

namespace CustomerSupportBot.Domain.Model.Auth;

public sealed record RefreshTokenInfo(
    string Id,
    string UserId,
    string TokenHash,
    DateTime ExpiresAt,
    DateTime CreatedAt,
    DateTime? RevokedAt,
    string? ReplacedByTokenHash);

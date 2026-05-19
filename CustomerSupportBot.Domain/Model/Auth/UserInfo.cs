// Domain/Model/Auth/UserInfo.cs
// Authenticated user snapshot — adapter-agnostic auth identity.

namespace CustomerSupportBot.Domain.Model.Auth;

public sealed record UserInfo(
    string Id,
    string Username,
    string PasswordHash,
    string Role,
    string? LinkedAgentId,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? LastLoginAt);

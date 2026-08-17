// Application/Ports/Driven/Auth/IRefreshTokenRepository.cs
// Secondary port — refresh token persistence.

using CustomerSupportBot.Domain.Model.Auth;

namespace CustomerSupportBot.Application.Ports.Outbound.Auth;

public interface IRefreshTokenRepository
{
    Task CreateAsync(string id, string userId, string tokenHash,
        DateTime expiresAt, DateTime createdAt, CancellationToken ct = default);

    Task<RefreshTokenInfo?> FindByHashAsync(string tokenHash, CancellationToken ct = default);

    Task RevokeAsync(string id, DateTime revokedAt,
        string? replacedByTokenHash, CancellationToken ct = default);
}

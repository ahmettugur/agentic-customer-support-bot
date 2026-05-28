// Application/Services/Auth/TokenPortService.cs
// ITokenService driving port implementasyonu.
// Refresh token lifecycle orkestrasyonu Application core'da; JWT imzalama IJwtAccessTokenProvider driven port'u üzerinden.

using System.Security.Cryptography;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Application.Ports.Inbound.Auth;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.Auth;

public sealed class TokenPortService : ITokenService
{
    private readonly IUserAuthRepository _users;
    private readonly IRefreshTokenRepository _tokens;
    private readonly IJwtAccessTokenProvider _jwt;
    private readonly JwtOptions _options;

    public TokenPortService(
        IUserAuthRepository users,
        IRefreshTokenRepository tokens,
        IJwtAccessTokenProvider jwt,
        IOptions<JwtOptions> options)
    {
        _users = users;
        _tokens = tokens;
        _jwt = jwt;
        _options = options.Value;
    }

    public async Task<AuthResponse> IssueAsync(UserInfo user, CancellationToken ct = default)
    {
        var (refreshPlain, refreshHash) = GenerateRefreshToken();
        var now = DateTime.UtcNow;
        var refreshExpiry = now.AddDays(_options.RefreshTokenDays);

        await _tokens.CreateAsync(Guid.NewGuid().ToString("N"), user.Id, refreshHash, refreshExpiry, now, ct);
        await _users.UpdateLastLoginAsync(user.Id, now, ct);

        var (access, accessExpiry) = _jwt.GenerateAccessToken(user, now);

        return new AuthResponse(access, refreshPlain, accessExpiry, refreshExpiry,
            user.Username, user.Role, user.LinkedAgentId);
    }

    public async Task<AuthResponse?> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return null;
        var hash = HashToken(refreshToken);

        var existing = await _tokens.FindByHashAsync(hash, ct);
        if (existing is null || existing.RevokedAt is not null || existing.ExpiresAt <= DateTime.UtcNow)
            return null;

        var user = await _users.FindByIdAsync(existing.UserId, ct);
        if (user is null || !user.IsActive) return null;

        var (newPlain, newHash) = GenerateRefreshToken();
        var now = DateTime.UtcNow;
        var newExpiry = now.AddDays(_options.RefreshTokenDays);

        await _tokens.RevokeAsync(existing.Id, now, newHash, ct);
        await _tokens.CreateAsync(Guid.NewGuid().ToString("N"), user.Id, newHash, newExpiry, now, ct);
        await _users.UpdateLastLoginAsync(user.Id, now, ct);

        var (access, accessExpiry) = _jwt.GenerateAccessToken(user, now);
        return new AuthResponse(access, newPlain, accessExpiry, newExpiry,
            user.Username, user.Role, user.LinkedAgentId);
    }

    public async Task<bool> RevokeAsync(string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return false;
        var hash = HashToken(refreshToken);

        var existing = await _tokens.FindByHashAsync(hash, ct);
        if (existing is null || existing.RevokedAt is not null) return false;

        await _tokens.RevokeAsync(existing.Id, DateTime.UtcNow, null, ct);
        return true;
    }

    private static (string Plain, string Hash) GenerateRefreshToken()
    {
        Span<byte> bytes = stackalloc byte[64];
        RandomNumberGenerator.Fill(bytes);
        var plain = Convert.ToBase64String(bytes);
        return (plain, HashToken(plain));
    }

    private static string HashToken(string token)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}

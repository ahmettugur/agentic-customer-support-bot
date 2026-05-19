// Adapters.Persistence/Auth/TokenService.cs
// JWT access + opaque refresh token üreten servis.
// ITokenService driving port'unun implementasyonu.

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CustomerSupportBot.Application.Ports.Driven.Auth;
using CustomerSupportBot.Application.Ports.Driving.Auth;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CustomerSupportBot.Adapters.Persistence.Auth;

public sealed class TokenService : ITokenService
{
    private readonly IUserAuthRepository _users;
    private readonly IRefreshTokenRepository _tokens;
    private readonly JwtOptions _options;
    private readonly ILogger<TokenService> _logger;

    public TokenService(
        IUserAuthRepository users,
        IRefreshTokenRepository tokens,
        IOptions<JwtOptions> options,
        ILogger<TokenService> logger)
    {
        _users = users;
        _tokens = tokens;
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.SigningKey) || _options.SigningKey.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey en az 32 karakter olmalı (HMAC-SHA256). appsettings içine ekleyin.");
        }
    }

    public async Task<AuthResponse> IssueAsync(UserInfo user, CancellationToken ct = default)
    {
        var (refreshPlain, refreshHash) = GenerateRefreshToken();
        var now = DateTime.UtcNow;
        var refreshExpiry = now.AddDays(_options.RefreshTokenDays);

        await _tokens.CreateAsync(
            Guid.NewGuid().ToString("N"), user.Id, refreshHash, refreshExpiry, now, ct);

        await _users.UpdateLastLoginAsync(user.Id, now, ct);

        var (access, accessExpiry) = GenerateAccessToken(user, now);

        return new AuthResponse(
            AccessToken: access,
            RefreshToken: refreshPlain,
            AccessTokenExpiresAt: accessExpiry,
            RefreshTokenExpiresAt: refreshExpiry,
            Username: user.Username,
            Role: user.Role,
            LinkedAgentId: user.LinkedAgentId);
    }

    public async Task<AuthResponse?> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return null;
        var hash = HashToken(refreshToken);

        var existing = await _tokens.FindByHashAsync(hash, ct);
        if (existing is null) return null;
        if (existing.RevokedAt is not null) return null;
        if (existing.ExpiresAt <= DateTime.UtcNow) return null;

        var user = await _users.FindByIdAsync(existing.UserId, ct);
        if (user is null || !user.IsActive) return null;

        // Rotate: eskiyi revoke et, yeni token üret
        var (newPlain, newHash) = GenerateRefreshToken();
        var now = DateTime.UtcNow;
        var newExpiry = now.AddDays(_options.RefreshTokenDays);

        await _tokens.RevokeAsync(existing.Id, now, newHash, ct);
        await _tokens.CreateAsync(Guid.NewGuid().ToString("N"), user.Id, newHash, newExpiry, now, ct);
        await _users.UpdateLastLoginAsync(user.Id, now, ct);

        var (access, accessExpiry) = GenerateAccessToken(user, now);
        return new AuthResponse(access, newPlain, accessExpiry, newExpiry, user.Username, user.Role, user.LinkedAgentId);
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

    // ─────────────────────────────────────────────────────────────────────────

    private (string Token, DateTime ExpiresAt) GenerateAccessToken(UserInfo user, DateTime nowUtc)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = nowUtc.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.UniqueName, user.Username),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };
        if (!string.IsNullOrWhiteSpace(user.LinkedAgentId))
            claims.Add(new Claim("linked_agent_id", user.LinkedAgentId));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: nowUtc,
            expires: expires,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
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
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}

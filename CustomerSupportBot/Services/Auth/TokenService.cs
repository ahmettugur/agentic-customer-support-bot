// Services/Auth/ITokenService.cs
// JWT access + opaque refresh token üreten servis.

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CustomerSupportBot.Infrastructure.Persistence;
using CustomerSupportBot.Infrastructure.Persistence.Entities.Auth;
using CustomerSupportBot.Models.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CustomerSupportBot.Services.Auth;

public interface ITokenService
{
    Task<AuthResponse> IssueAsync(UserEntity user, CancellationToken ct = default);
    Task<AuthResponse?> RefreshAsync(string refreshToken, CancellationToken ct = default);
    Task<bool> RevokeAsync(string refreshToken, CancellationToken ct = default);
}

public sealed class TokenService : ITokenService
{
    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;
    private readonly JwtOptions _options;
    private readonly ILogger<TokenService> _logger;

    public TokenService(
        IDbContextFactory<CustomerSupportDbContext> dbFactory,
        IOptions<JwtOptions> options,
        ILogger<TokenService> logger)
    {
        _dbFactory = dbFactory;
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.SigningKey) || _options.SigningKey.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey en az 32 karakter olmalı (HMAC-SHA256). appsettings içine ekleyin.");
        }
    }

    public async Task<AuthResponse> IssueAsync(UserEntity user, CancellationToken ct = default)
    {
        var (refreshPlain, refreshHash) = GenerateRefreshToken();
        var now = DateTime.UtcNow;
        var refreshExpiry = now.AddDays(_options.RefreshTokenDays);

        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        ctx.RefreshTokens.Add(new RefreshTokenEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = user.Id,
            TokenHash = refreshHash,
            ExpiresAt = refreshExpiry,
            CreatedAt = now
        });

        // Last login timestamp güncelle (kullanıcı tracked değil — Attach + Update Property)
        var tracked = await ctx.Users.FirstAsync(u => u.Id == user.Id, ct);
        tracked.LastLoginAt = now;
        await ctx.SaveChangesAsync(ct);

        var (access, accessExpiry) = GenerateAccessToken(user, now);

        return new AuthResponse(
            AccessToken: access,
            RefreshToken: refreshPlain,
            AccessTokenExpiresAt: accessExpiry,
            RefreshTokenExpiresAt: refreshExpiry,
            Username: user.Username,
            Role: user.Role);
    }

    public async Task<AuthResponse?> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return null;
        var hash = HashToken(refreshToken);

        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        var existing = await ctx.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (existing is null) return null;
        if (existing.RevokedAt is not null) return null;
        if (existing.ExpiresAt <= DateTime.UtcNow) return null;

        var user = await ctx.Users.FirstOrDefaultAsync(u => u.Id == existing.UserId, ct);
        if (user is null || !user.IsActive) return null;

        // Rotate: eskiyi revoke et, yeni token üret
        var (newPlain, newHash) = GenerateRefreshToken();
        var now = DateTime.UtcNow;
        var newExpiry = now.AddDays(_options.RefreshTokenDays);

        existing.RevokedAt = now;
        existing.ReplacedByTokenHash = newHash;

        ctx.RefreshTokens.Add(new RefreshTokenEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = user.Id,
            TokenHash = newHash,
            ExpiresAt = newExpiry,
            CreatedAt = now
        });

        user.LastLoginAt = now;
        await ctx.SaveChangesAsync(ct);

        var (access, accessExpiry) = GenerateAccessToken(user, now);
        return new AuthResponse(access, newPlain, accessExpiry, newExpiry, user.Username, user.Role);
    }

    public async Task<bool> RevokeAsync(string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return false;
        var hash = HashToken(refreshToken);

        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        var existing = await ctx.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (existing is null || existing.RevokedAt is not null) return false;

        existing.RevokedAt = DateTime.UtcNow;
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private (string Token, DateTime ExpiresAt) GenerateAccessToken(UserEntity user, DateTime nowUtc)
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

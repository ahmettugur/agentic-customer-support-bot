// Application/Services/Auth/TokenPortService.cs
// ITokenService driving port implementasyonu.
// Refresh token lifecycle orkestrasyonu Application core'da; JWT imzalama IJwtAccessTokenProvider driven port'u üzerinden.

using System.Security.Cryptography;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
using CustomerSupportBot.Application.Ports.Inbound.Auth;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.Auth;

public sealed class TokenPortService : ITokenService
{
    private readonly IUserAuthRepository _users;
    private readonly IRefreshTokenRepository _tokens;
    private readonly IJwtAccessTokenProvider _jwt;
    private readonly ICustomerRepository? _customers;
    private readonly JwtOptions _options;

    public TokenPortService(
        IUserAuthRepository users,
        IRefreshTokenRepository tokens,
        IJwtAccessTokenProvider jwt,
        IOptions<JwtOptions> options,
        ICustomerRepository? customers = null)
    {
        _users = users;
        _tokens = tokens;
        _jwt = jwt;
        _customers = customers;
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
            user.Username, user.Role, user.LinkedAgentId,
            await ResolveFullNameAsync(user, ct));
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

        // Koşullu sahiplenme: yalnızca token HÂLÂ iptal edilmemişse iptal et. Eşzamanlı gelen
        // ikinci bir yenileme isteği (aynı çalıntı/paylaşılan token'la) burada false alır ve
        // reddedilir — okuma anında ikisi de "geçerli" görmüş olsa bile, tek bir token'dan iki
        // ayrı oturum zinciri doğmaz.
        if (!await _tokens.TryRevokeAsync(existing.Id, now, newHash, ct))
            return null;

        await _tokens.CreateAsync(Guid.NewGuid().ToString("N"), user.Id, newHash, newExpiry, now, ct);
        await _users.UpdateLastLoginAsync(user.Id, now, ct);

        var (access, accessExpiry) = _jwt.GenerateAccessToken(user, now);
        return new AuthResponse(access, newPlain, accessExpiry, newExpiry,
            user.Username, user.Role, user.LinkedAgentId,
            await ResolveFullNameAsync(user, ct));
    }

    /// <summary>
    /// Müşteri hesapları için katalogdaki adı soyadı; staff (Admin/Agent) hesaplarında null.
    ///
    /// <para>
    /// Kimlik <see cref="UserInfo.LinkedCustomerId"/>'den — yani JWT'nin bağlı olduğu hesaptan —
    /// çözülür, istekten gelen hiçbir değerden değil; bu yüzden başka bir müşterinin adı bu
    /// yolla dönemez. Repo çözülemezse veya kayıt yoksa null döner: ad yalnızca gösterim
    /// amaçlı olduğu için eksikliği login akışını bozmamalı.
    /// </para>
    /// </summary>
    private async Task<string?> ResolveFullNameAsync(UserInfo user, CancellationToken ct)
    {
        if (_customers is null) return null;
        if (string.IsNullOrWhiteSpace(user.LinkedCustomerId)) return null;
        if (!long.TryParse(user.LinkedCustomerId, out var customerId)) return null;

        return await _customers.GetFullNameAsync(customerId, ct);
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

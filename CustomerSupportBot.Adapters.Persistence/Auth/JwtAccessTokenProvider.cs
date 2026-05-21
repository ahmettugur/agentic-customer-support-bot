// Adapters.Persistence/Auth/JwtAccessTokenProvider.cs
// IJwtAccessTokenProvider driven port implementasyonu.
// IdentityModel bağımlılığı burada izole edilmiştir; core bilmez.

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CustomerSupportBot.Application.Ports.Driven.Auth;
using CustomerSupportBot.Domain.Model.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CustomerSupportBot.Adapters.Persistence.Auth;

public sealed class JwtAccessTokenProvider : IJwtAccessTokenProvider
{
    private readonly JwtOptions _options;

    public JwtAccessTokenProvider(IOptions<JwtOptions> options)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.SigningKey) || _options.SigningKey.Length < 32)
            throw new InvalidOperationException(
                "Jwt:SigningKey en az 32 karakter olmalı (HMAC-SHA256). appsettings içine ekleyin.");
    }

    public (string Token, DateTime ExpiresAt) GenerateAccessToken(UserInfo user, DateTime nowUtc)
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
}

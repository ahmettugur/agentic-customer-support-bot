// Adapters.Persistence/Auth/JwtAccessTokenProvider.cs
// IJwtAccessTokenProvider driven port implementasyonu.
// IdentityModel bağımlılığı burada izole edilmiştir; core bilmez.

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CustomerSupportBot.Application.Ports.Outbound.Auth;
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
        if (JwtOptions.ValidateSigningKey(_options.SigningKey) is { } error)
            throw new InvalidOperationException(error);
    }

    public (string Token, DateTime ExpiresAt) GenerateAccessToken(UserInfo user, DateTime nowUtc, int? lifetimeMinutes = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = nowUtc.AddMinutes(lifetimeMinutes ?? _options.AccessTokenMinutes);

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
        if (!string.IsNullOrWhiteSpace(user.LinkedCustomerId))
            claims.Add(new Claim("linked_customer_id", user.LinkedCustomerId));

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

using CustomerSupportBot.Domain.Model.Auth;

namespace CustomerSupportBot.Application.Ports.Driven.Auth;

/// <summary>
/// JWT access token üretimi için secondary (driven) port.
/// </summary>
public interface IJwtAccessTokenProvider
{
    (string Token, DateTime ExpiresAt) GenerateAccessToken(UserInfo user, DateTime nowUtc);
}

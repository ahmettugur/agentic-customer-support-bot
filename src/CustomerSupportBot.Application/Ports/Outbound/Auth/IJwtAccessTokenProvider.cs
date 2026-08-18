using CustomerSupportBot.Domain.Model.Auth;

namespace CustomerSupportBot.Application.Ports.Outbound.Auth;

/// <summary>
/// JWT access token üretimi için secondary (driven) port.
/// </summary>
public interface IJwtAccessTokenProvider
{
    /// <summary>
    /// Erişim token'ı üretir.
    /// </summary>
    /// <param name="lifetimeMinutes">
    /// Token ömrü. <c>null</c> ise yapılandırmadaki varsayılan (<c>AccessTokenMinutes</c>) kullanılır —
    /// mevcut tüm çağıranlar bu davranışı korur. A2A özne token'ları gibi tek bir çağrı için üretilen,
    /// dış sisteme verilen token'lar bilinçli olarak çok daha kısa bir ömürle istenir.
    /// </param>
    (string Token, DateTime ExpiresAt) GenerateAccessToken(UserInfo user, DateTime nowUtc, int? lifetimeMinutes = null);
}

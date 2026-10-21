// Application/Services/A2A/PartnerChannels.cs
// Partner kanal adları ve token'dan partner kimliğinin çözülmesi.

using System.Security.Claims;

namespace CustomerSupportBot.Application.Services.A2A;

public static class PartnerChannels
{
    public const string A2A = "a2a";
    public const string Mcp = "mcp";
}

public static class A2APartnerIdentity
{
    /// <summary>
    /// Çağıran partner. Özne token'ında kimlik <c>a2a:{partner}:{müşteri}</c> biçimindedir; partner token'ında
    /// <c>NameIdentifier</c> kullanıcı satırının GUID'idir, partner adı kullanıcı adındadır (yapılandırmadaki
    /// <c>PartnerId</c> ve token değişimi de buradan okur). Çözülemezse <c>null</c>.
    /// </summary>
    public static string? Resolve(ClaimsPrincipal user) =>
        A2ASubjectIdentity.TryGetPartnerId(user.FindFirst(ClaimTypes.NameIdentifier)?.Value)
        ?? user.FindFirst(ClaimTypes.Name)?.Value;
}

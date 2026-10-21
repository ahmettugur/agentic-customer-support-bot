// Ports/Outbound/A2A/IPartnerChannelAuthorizer.cs
// Hangi partner hangi dış kanalı (a2a, mcp) kullanabilir.

namespace CustomerSupportBot.Application.Ports.Outbound.A2A;

/// <summary>
/// Kanal yetkisi token'a değil partner YAPILANDIRMASINA bağlıdır ve her istekte okunur: erişim yapılandırma
/// değişince token süresi beklenmeden kesilir, ele geçirilmiş bir token da kendi kanalını genişletemez.
/// <see cref="IA2ASubjectAuthorizer"/> ile (hangi MÜŞTERİ) birbirini tamamlar: o "kimin adına", bu "hangi kapıdan".
/// </summary>
public interface IPartnerChannelAuthorizer
{
    bool CanUse(string? partnerId, string channel);

    /// <summary>Partnerin en az bir kanalı var mı (token değişimi için; kanalsız partner tamamen kapalıdır).</summary>
    bool CanUseAny(string? partnerId);
}

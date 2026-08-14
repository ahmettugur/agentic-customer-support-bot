// Application/Services/Chat/SessionIdentityBinder.cs
// Bir oturuma JWT-doğrulanmış müşteri kimliğini bağlar ve oturum sahipliğini doğrular.

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Chat;

/// <summary>
/// Oturum ↔ müşteri bağını kuran tek nokta.
///
/// <para>
/// <b>Neden ayrı bir yardımcı:</b> aynı bağlama üç kanalda gerekiyor — yazılı chat, sesli köprü
/// modu ve sesli native mod. Kural üç yere kopyalansaydı biri güncellenip diğerleri kalırdı;
/// nitekim sesli kanallar uzun süre bu bağı hiç kurmuyordu ve sonucunda sipariş sorgulayan her
/// tool <c>customerId=""</c> ile çalışıp "sipariş bulunamadı" dönüyordu.
/// </para>
/// </summary>
public static class SessionIdentityBinder
{
    /// <summary>
    /// Oturumu login'li müşteriye bağlar.
    ///
    /// <para>
    /// Bağ <b>bir kez</b> kurulur; oturum zaten aynı müşteriye bağlıysa işlem yapılmaz. Oturum
    /// BAŞKA bir müşteriye bağlıysa <c>false</c> döner — çağıran taraf bağlantıyı reddetmelidir.
    /// Bu kontrol olmadan, bir kullanıcı başkasının <c>sessionId</c>'sini vererek o oturumun
    /// kimliğiyle çalışan tool'lara (sipariş geçmişi, iptal, iade) erişebilirdi: oturum zaten
    /// bağlı olduğu için bağlama sessizce atlanır ve tool'lar oturumdaki kimlikle koşardı.
    /// </para>
    /// </summary>
    /// <param name="authenticatedCustomerId">
    /// JWT claim'inden gelen kimlik. Boşsa oturum bağlanmaz ama reddedilmez de — anonim
    /// kullanımın açıkça desteklendiği akışlar bozulmasın diye (tool'lar zaten kimliksiz
    /// çalışamaz, kendi doğrulamalarında reddederler).
    /// </param>
    /// <returns>Oturum bu kullanıcı tarafından kullanılabiliyorsa <c>true</c>.</returns>
    public static async Task<bool> TryBindAsync(
        AgentSession session,
        string? authenticatedCustomerId,
        ISessionManager sessions,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(authenticatedCustomerId))
            return true;

        var bound = session.State.AuthenticatedCustomerId;

        if (string.IsNullOrWhiteSpace(bound))
        {
            session.State.AuthenticatedCustomerId = authenticatedCustomerId;
            await sessions.UpdateAsync(session, ct);
            return true;
        }

        return string.Equals(bound, authenticatedCustomerId, StringComparison.Ordinal);
    }
}

// Application/Services/Chat/SessionIdentityBinder.cs
// Bir oturuma JWT-doğrulanmış müşteri kimliğini bağlar ve oturum sahipliğini doğrular.

using CustomerSupportBot.Application.Ports.Outbound.Locking;
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
    /// <summary>
    /// Oturum kimliği bağlamanın kullanılması gereken tur/bağlantı kilidi anahtarı.
    ///
    /// <para>
    /// <c>session:{id}</c> DEĞİL: o anahtar oturum durumu yazan alt işlemler tarafından
    /// (<c>MutateStateAsync</c>, <c>AddExchangeAsync</c>) içeriden alınıyor ve Redis kilidi
    /// yeniden girişli olmadığı için aynı anahtarı dışarıda tutmak kendi kendine kilitlenme
    /// üretirdi.
    /// </para>
    /// </summary>
    public static string TurnLockKey(string sessionId) => $"session-turn:{sessionId}";

    /// <summary>
    /// Kimlik bağlamayı <b>atomik</b> yapar: kilidi alır, oturumu kalıcı depodan tazeler,
    /// sonra bağlar.
    ///
    /// <para>
    /// Üç adımın birlikte olması şart, çünkü bağlama bir "oku-karar ver-yaz" dizisidir.
    /// Kilitsiz hâlde sahipsiz aynı oturuma eşzamanlı gelen iki farklı müşteri de
    /// "bağlı değil" görüp ikisi de bağlamayı deneyebilir. Tazeleme de gerekli: cache-first
    /// okuma, kilidi beklerken başka bir pod'un yaptığı güncellemeyi görmez — uzak güncelleme
    /// cache'e YENİ bir nesne koyar, bekleyenin elindeki referans eskir.
    /// </para>
    ///
    /// <para>
    /// Yazılı sohbet turu bu metodu çağırmaz çünkü kilidi <b>tur boyunca</b> tutar ve bağlamayı
    /// zaten o kilidin altında yapar; ikisi <see cref="TurnLockKey"/> ile aynı anahtarı
    /// paylaştığı için birbirini dışlar. Realtime kanalları ise kilidi bağlantı ömrü boyunca
    /// tutamaz (bağlantı dakikalarca sürer), o yüzden yalnızca bağlama anını kilitler.
    /// </para>
    /// </summary>
    public static async Task<AgentSession?> BindAtomicallyAsync(
        string sessionId,
        string? authenticatedCustomerId,
        ISessionManager sessions,
        IAppDistributedLock locks,
        CancellationToken ct = default)
    {
        await using var handle = await locks.AcquireAsync(TurnLockKey(sessionId), ct: ct);

        var session = await sessions.ReloadAsync(sessionId, ct);
        return await TryBindAsync(session, authenticatedCustomerId, sessions, ct) ? session : null;
    }

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

    /// <summary>
    /// Salt-okunur sahiplik kontrolü — oturumu <b>oluşturmaz ve değiştirmez</b>.
    ///
    /// <para>
    /// <see cref="TryBindAsync"/> yazma yolları içindir (bir tur başlatmak, sesli bağlantı
    /// açmak). Bu ise yalnızca okuyan uçlar içindir: olay akışına abone olmak, okunmamış onay
    /// bildirimlerini çekmek. Oradan <c>GetOrCreateAsync</c> çağırmak, rastgele bir
    /// <c>sessionId</c> verilerek boş oturum üretilmesine yol açardı.
    /// </para>
    /// </summary>
    /// <returns>
    /// Oturum yoksa, henüz kimseye bağlı değilse veya bu müşteriye aitse <c>true</c>.
    /// </returns>
    public static async Task<bool> IsAccessibleAsync(
        string? sessionId,
        string? authenticatedCustomerId,
        ISessionManager sessions,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return true;
        if (string.IsNullOrWhiteSpace(authenticatedCustomerId)) return true;

        var session = await sessions.GetAsync(sessionId, ct);
        if (session is null) return true;

        var bound = session.State.AuthenticatedCustomerId;
        return string.IsNullOrWhiteSpace(bound)
            || string.Equals(bound, authenticatedCustomerId, StringComparison.Ordinal);
    }
}

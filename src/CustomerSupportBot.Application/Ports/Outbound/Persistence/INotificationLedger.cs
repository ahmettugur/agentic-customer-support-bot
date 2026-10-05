// Ports/Outbound/Persistence/INotificationLedger.cs
// Gönderilen bildirimlerin defteri — çok pod'lu kurulumda bir bildirimin bir kez gönderilmesi için.

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

/// <summary>
/// Bildirimi tetikleyen olaylar birden fazla pod'da işlenebilir (ör. onay kararı pub/sub ile tüm pod'lara
/// yayılır). Göndermeden önce bildirimin anahtarı (ör. <c>approval-result:{id}</c>) talep edilir; yalnızca
/// talebi kazanan pod gönderir. Talep atomiktir (kalıcı depoda benzersiz anahtar).
/// </summary>
public interface INotificationLedger
{
    /// <summary>Anahtarı ilk kez talep eden <c>true</c> alır; daha önce talep edilmişse <c>false</c>.</summary>
    Task<bool> TryClaimAsync(string key, CancellationToken ct = default);

    /// <summary>Gönderim başarısız olunca talebi geri bırakır — bildirim yeniden denenebilir.</summary>
    Task ReleaseAsync(string key, CancellationToken ct = default);
}

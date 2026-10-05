using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

/// <summary>Sohbet fotoğraflarının kalıcılığı.</summary>
public interface IAttachmentStore
{
    Task SaveAsync(ChatAttachment attachment, CancellationToken ct = default);

    /// <summary>Görüntü verisiyle birlikte tek kayıt; yoksa <c>null</c>.</summary>
    Task<ChatAttachment?> GetAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Oturumun fotoğrafları — görüntü verisi OLMADAN (<see cref="ChatAttachment.Data"/> boş), en eskiden
    /// yeniye. Sayım, açıklama ve onaya bağlama için.
    /// </summary>
    Task<IReadOnlyList<ChatAttachment>> ListForSessionAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Henüz gönderilmemiş kayıtları "mesajla gönderildi" olarak işaretler.</summary>
    Task MarkSentAsync(IReadOnlyCollection<string> ids, DateTime sentAt, CancellationToken ct = default);

    /// <summary>Kaydı siler — yalnızca henüz gönderilmemişse. Silindiyse <c>true</c>.</summary>
    Task<bool> DeleteUnsentAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// <paramref name="cutoffUtc"/>'den önce yüklenmiş fotoğrafları siler (veri saklama süresi); silinen
    /// sayıyı döner. Oturum daha yeni olsa da silinir — fotoğraf en hassas veridir.
    /// </summary>
    Task<int> DeleteCreatedBeforeAsync(DateTime cutoffUtc, CancellationToken ct = default);

    /// <summary>Henüz bir onaya bağlanmamış kayıtları <paramref name="approvalId"/>'ye bağlar.</summary>
    Task LinkToApprovalAsync(IReadOnlyCollection<string> ids, string approvalId, CancellationToken ct = default);
}

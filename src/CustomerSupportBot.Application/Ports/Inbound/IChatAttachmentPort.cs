// Application/Ports/Inbound/IChatAttachmentPort.cs
// Müşterinin sohbete fotoğraf eklemesi.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Inbound;

public interface IChatAttachmentPort
{
    /// <summary>
    /// Fotoğrafı doğrular, meta verisini siler, açıklamasını üretir ve kaydeder. <paramref name="sessionId"/>
    /// boşsa yeni oturum açılır ve müşteriye bağlanır.
    /// </summary>
    Task<AttachmentUploadResult> UploadAsync(
        string? sessionId, string? authenticatedCustomerId, byte[] data, CancellationToken ct = default);

    /// <summary>
    /// Görüntü verisiyle fotoğraf. <paramref name="customerId"/> verilirse yalnızca o müşterinin fotoğrafı
    /// döner (müşteri ucu); <c>null</c> ise sahiplik kontrolü yapılmaz (personel uçları).
    /// </summary>
    Task<ChatAttachment?> GetAsync(string id, string? customerId, CancellationToken ct = default);

    /// <summary>
    /// Müşterinin henüz göndermediği fotoğrafını siler (önizlemeden kaldırma). Gönderilmiş fotoğraf
    /// geçmişte ve olası bir onay kaydında yer aldığı için silinmez. Silindiyse <c>true</c>.
    /// </summary>
    Task<bool> DeleteUnsentAsync(string id, string customerId, CancellationToken ct = default);
}

public enum AttachmentUploadStatus
{
    Ok,
    Disabled,
    InvalidSession,
    Forbidden,
    Empty,
    TooLarge,
    UnsupportedType,
    TooManyInSession
}

/// <param name="Error">Başarısızsa kullanıcıya gösterilecek Türkçe açıklama.</param>
public sealed record AttachmentUploadResult(
    AttachmentUploadStatus Status,
    string? AttachmentId = null,
    string? SessionId = null,
    string? Description = null,
    string? Error = null);

// EfCore/Entities/Chat/AttachmentEntity.cs
// `chat.attachments` tablosu — müşterinin sohbette eklediği fotoğraflar (meta verisi silinmiş).

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Chat;

public sealed class AttachmentEntity
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string? CustomerId { get; set; }
    public string ContentType { get; set; } = "";
    public int SizeBytes { get; set; }
    public byte[] Data { get; set; } = [];
    public string? Description { get; set; }
    public DateTime? SentAt { get; set; }
    public string? ApprovalId { get; set; }
    public DateTime CreatedAt { get; set; }
}

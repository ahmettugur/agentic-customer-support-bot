// Ports/Inbound/IDataPrivacyPort.cs
// Kişisel veri: otomatik saklama süresi temizliği, müşterinin verisini dışa aktarma ve silme (KVKK).

using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Memory;

namespace CustomerSupportBot.Application.Ports.Inbound;

public interface IDataPrivacyPort
{
    /// <summary>
    /// Süresi dolan fotoğrafları ve hareketsiz oturumları siler (<c>DataRetention</c> ayarları).
    /// Kapalıysa hiçbir şey yapmaz.
    /// </summary>
    Task<RetentionResult> RunRetentionAsync(DateTime nowUtc, CancellationToken ct = default);

    /// <summary>Müşterinin tüm kişisel verisi — görüntü verisi hariç, okunabilir JSON için.</summary>
    Task<CustomerDataExport> ExportCustomerDataAsync(string customerId, CancellationToken ct = default);

    /// <summary>
    /// Müşterinin sohbet ve profil verisini siler. Sipariş, şikayet ve onay kayıtları yasal/işlemsel kayıt
    /// olarak kalır. Bir depo hata verirse <see cref="DataErasureException"/> fırlatılır; işlem güvenle
    /// tekrarlanabilir.
    /// </summary>
    Task<ErasureResult> EraseCustomerDataAsync(string customerId, CancellationToken ct = default);
}

public sealed record RetentionResult(bool Enabled, int SessionsErased, int AttachmentsDeleted, IReadOnlyList<string> Failures);

public sealed record ErasureResult(string CustomerId, int SessionsErased, IReadOnlyDictionary<string, int> RecordsByStore);

/// <summary>Silme kısmen başarısız oldu — hangi depoların başarısız olduğu <see cref="FailedStores"/>'da.</summary>
public sealed class DataErasureException(IReadOnlyList<string> failedStores, Exception? inner = null)
    : Exception($"Kişisel veri silme kısmen başarısız: {string.Join(", ", failedStores)}", inner)
{
    public IReadOnlyList<string> FailedStores { get; } = failedStores;
}

public sealed record CustomerDataExport(
    string CustomerId,
    DateTime ExportedAtUtc,
    CustomerProfile? Profile,
    IReadOnlyList<ExportedSession> Sessions,
    IReadOnlyList<ConversationRating> Ratings,
    IReadOnlyList<ExportedApproval> Approvals,
    IReadOnlyList<ExportedOrder> Orders,
    IReadOnlyList<ExportedComplaint> Complaints);

public sealed record ExportedSession(
    string SessionId,
    DateTime CreatedAt,
    DateTime LastActivity,
    IReadOnlyList<ConversationMessage> Messages,
    IReadOnlyList<ExportedAttachment> Attachments,
    IReadOnlyList<ExportedDisposition>? Dispositions = null);

/// <summary>Temsilcinin sohbeti kapatırken kaydettiği neden/etiket/not (kapatan temsilcinin adı hariç).</summary>
public sealed record ExportedDisposition(string ReasonCode, IReadOnlyList<string> Tags, string? Note, DateTime ClosedAt);

/// <summary>Fotoğrafın bilgileri — görüntü verisi dışa aktarmaya konmaz (müşteri sohbette görebilir).</summary>
public sealed record ExportedAttachment(string Id, string ContentType, string? Description, DateTime CreatedAt, DateTime? SentAt);

public sealed record ExportedApproval(
    string Id, string ToolName, string Status, DateTime RequestedAt, DateTime? DecidedAt, string? DecisionReason);

public sealed record ExportedOrder(string OrderId, string Status, DateTime OrderDate, string Lines);

public sealed record ExportedComplaint(string ComplaintId, string OrderId, string Complaint, string Status);

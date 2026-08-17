// Domain/Model/ExtractedIds.cs
// Kullanıcı mesajından regex ile çıkarılmış entity ID'leri.

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// Metinden çıkarılmış entity ID'leri.
/// IdExtractor tarafından kullanıcı mesajından deterministik olarak çıkarılır.
/// </summary>
public class ExtractedIds
{
    public string? OrderId { get; set; }
    public string? CustomerId { get; set; }
    public string? ComplaintId { get; set; }

    /// <summary>
    /// True yalnızca CustomerId, metinde hiçbir bağlam kelimesi (sipariş/şikayet/müşteri/numaram)
    /// bulunmadığı için "kısa sorguda tek sayı → customer_id" varsayımıyla atandığında.
    /// Gerçek bir "müşteri" sinyali değil, zayıf bir varsayımdır — çağıran (EntityVerifier)
    /// bu durumda konuşma geçmişindeki bağlamı tercih edebilir.
    /// </summary>
    public bool IsCustomerIdAssumed { get; set; }

    public bool HasAny =>
        !string.IsNullOrEmpty(OrderId) ||
        !string.IsNullOrEmpty(CustomerId) ||
        !string.IsNullOrEmpty(ComplaintId);
}

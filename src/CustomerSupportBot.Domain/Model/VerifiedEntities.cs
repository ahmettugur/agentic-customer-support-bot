// Models/VerifiedEntities.cs
// Entity resolution.
// EntityVerifier, session.State.AuthenticatedCustomerId'yi (JWT'den) VerifiedEntities.CustomerId
// olarak taşır. Sipariş/şikayet ID'si artık burada üretilmez (bkz. IdExtractor'ın kaldırılması) —
// order_id/complaint_id çözümü tamamen LLM'e bırakılmıştır; OrderId/ComplaintId alanları
// SubTaskOrchestrator gibi başka kaynaklardan (yapılandırılmış alt-görev entity'leri) doldurulabilir.

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// Mevcut turda (session state, alt-görev entity'leri vb.) çözümlenmiş entity'lerin
/// yapılandırılmış özeti. ReasoningService prompt'una enjekte edilir.
/// </summary>
public class VerifiedEntities
{
    /// <summary>Çözümlenmiş sipariş ID'si (ör. 1030); varlık/sahiplik tool'da doğrulanır.</summary>
    public VerifiedEntity? OrderId { get; set; }

    /// <summary>Authenticated session'dan alınan müşteri ID'si (ör. 1027).</summary>
    public VerifiedEntity? CustomerId { get; set; }

    /// <summary>Çözümlenmiş şikayet ID'si (ör. 1001); varlık/sahiplik tool'da doğrulanır.</summary>
    public VerifiedEntity? ComplaintId { get; set; }

    /// <summary>
    /// Geriye dönük uyumluluk alanı. EntityVerifier artık eager DB sorgusu yapmadığı için
    /// bu alanı üretmez; son sipariş specialist tool ile okunur.
    /// </summary>
    public string? DerivedLastOrderId { get; set; }

    /// <summary>
    /// Geriye dönük uyumluluk alanı. EntityVerifier artık bu alanı üretmez.
    /// </summary>
    public int? DerivedOrderCount { get; set; }

    /// <summary>En az bir çözümlenmiş entity var mı?</summary>
    public bool HasAny =>
        OrderId != null || CustomerId != null || ComplaintId != null;

    /// <summary>En az bir güvenilir sistem kaynağıyla doğrulanmış entity var mı?</summary>
    public bool HasAnyVerified =>
        (OrderId?.Verification == EntityVerification.Verified) ||
        (CustomerId?.Verification == EntityVerification.Verified) ||
        (ComplaintId?.Verification == EntityVerification.Verified);
}

/// <summary>
/// Tek bir entity için doğrulama sonucu.
/// </summary>
public class VerifiedEntity
{
    /// <summary>Entity değeri (ör. "1030").</summary>
    public string Value { get; set; } = "";

    /// <summary>Bu değer nereden elde edildi?</summary>
    public EntitySource Source { get; set; }

    /// <summary>Çözümleme/doğrulama seviyesi.</summary>
    public EntityVerification Verification { get; set; }

    /// <summary>
    /// Harici bir doğrulayıcı Verified sonucu sağladıysa kısa özet attribute'ları.
    /// Ör. order için { "status": "Kargolandı", "product": "Dell XPS 15", "customerId": "1027" }.
    /// </summary>
    public Dictionary<string, string>? Attributes { get; set; }
}

/// <summary>Entity'nin hangi kaynaktan çıkarıldığı.</summary>
public enum EntitySource
{
    /// <summary>Güncel kullanıcı mesajından.</summary>
    Query,
    /// <summary>Önceki konuşma turundan.</summary>
    History,
    /// <summary>Oturum durumundan (SessionState.AuthenticatedCustomerId — JWT'den).</summary>
    SessionState,
    /// <summary>Başka bir entity'den türetilmiş (ör. customer_id'den last_order).</summary>
    Derived
}

/// <summary>Doğrulama seviyesi.</summary>
public enum EntityVerification
{
    /// <summary>Güvenilir sistem kaynağıyla doğrulanmış (ör. authenticated session).</summary>
    Verified,
    /// <summary>Format doğru ama DB'de bulunamadı (kullanıcı yanlış numara vermiş olabilir).</summary>
    NotFoundInDb,
    /// <summary>Format/bağlam çözümlendi; gerçeklik ve sahiplik specialist tool'a ertelendi.</summary>
    FormatOnly
}

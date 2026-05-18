// Models/VerifiedEntities.cs
// Entity grounding / ReAct-lite.
// IdExtractor sadece regex formatını doğrular ("ORD-1" mi?).
// EntityVerifier ise bunun DB'de var olduğunu da doğrular ("ORD-1" gerçekten bir sipariş mi?)
// Ve reasoning modeline verilen prompt'a "doğrulanmış bağlam" olarak enjekte eder.
// Amaç: hallucination'ı düşürmek ve requiredInfo'nun zaten bilinen alanları istememesini
// Garanti altına almak.

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// Mevcut turda (query + history + session state) tespit edilmiş ve doğrulanmış
/// Entity'lerin yapılandırılmış özeti. ReasoningService prompt'una enjekte edilir.
/// </summary>
public class VerifiedEntities
{
    /// <summary>Doğrulanmış sipariş ID'si (ör. ORD-1).</summary>
    public VerifiedEntity? OrderId { get; set; }

    /// <summary>Doğrulanmış müşteri ID'si (ör. CUST-1990" veya CUST-001).</summary>
    public VerifiedEntity? CustomerId { get; set; }

    /// <summary>Doğrulanmış şikayet ID'si (ör. CMP-1).</summary>
    public VerifiedEntity? ComplaintId { get; set; }

    /// <summary>
    /// Türetilmiş alan: customer_id doğrulanmışsa müşterinin en son sipariş ID'si.
    /// Plan yapan modelin "get_last_order_tool çağırayım" kararını önceden
    /// Grounded bilgiyle verebilmesi için.
    /// </summary>
    public string? DerivedLastOrderId { get; set; }

    /// <summary>
    /// Türetilmiş alan: customer_id doğrulanmışsa toplam sipariş sayısı.
    /// </summary>
    public int? DerivedOrderCount { get; set; }

    /// <summary>En az bir doğrulanmış entity var mı?</summary>
    public bool HasAny =>
        OrderId != null || CustomerId != null || ComplaintId != null;

    /// <summary>En az bir DB-verified entity var mı (sadece format değil)?</summary>
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
    /// <summary>Entity değeri (ör. "ORD-1").</summary>
    public string Value { get; set; } = "";

    /// <summary>Bu değer nereden elde edildi?</summary>
    public EntitySource Source { get; set; }

    /// <summary>DB ile doğrulama durumu.</summary>
    public EntityVerification Verification { get; set; }

    /// <summary>
    /// Verified ise entity'nin DB'den çekilmiş kısa özet attribute'ları.
    /// Ör. order için { "status": "Kargolandı", "product": "Dell XPS 15", "customerId": "CUST-1990"" }.
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
    /// <summary>Oturum durumundan (SessionState.CustomerId vb.).</summary>
    SessionState,
    /// <summary>Başka bir entity'den türetilmiş (ör. customer_id'den last_order).</summary>
    Derived
}

/// <summary>Doğrulama seviyesi.</summary>
public enum EntityVerification
{
    /// <summary>Format doğru + DB'de mevcut.</summary>
    Verified,
    /// <summary>Format doğru ama DB'de bulunamadı (kullanıcı yanlış numara vermiş olabilir).</summary>
    NotFoundInDb,
    /// <summary>Format doğrulandı, DB verification uygulanmadı (ör. customer_id için opsiyonel).</summary>
    FormatOnly
}


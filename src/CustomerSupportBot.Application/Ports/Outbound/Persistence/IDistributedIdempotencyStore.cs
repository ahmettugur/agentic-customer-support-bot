namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

/// <summary>
/// Yan etkili tool çağrılarının pod'lar arasında paylaşılan, kısa ömürlü kaydı —
/// <see cref="Services.Tools.SideEffectIdempotencyCache"/>'in ikinci (dağıtık) katmanı.
/// </summary>
/// <remarks>
/// Bellek içi cache tek pod'u görür: aynı müşterinin yinelenen isteği (çift gönderim, yeniden
/// deneme) yük dengeleyici tarafından başka bir pod'a düşürülürse orada iz yoktur ve kayıt iki
/// kez oluşur. Bu port, bu pencereyi tüm pod'lar için ortak tutar.
/// </remarks>
public interface IDistributedIdempotencyStore
{
    /// <summary>Anahtar için kaydedilmiş varlık kimliği (sipariş/şikayet no); yoksa <c>null</c>.</summary>
    string? Get(string key);

    /// <summary>
    /// Anahtarı varlık kimliğiyle kaydeder. Anahtar zaten varsa DEĞİŞTİRMEZ — ilk oluşturulan
    /// kayıt kanoniktir. <paramref name="ttl"/> sonunda kayıt kendiliğinden düşer.
    /// </summary>
    void Set(string key, string entityId, TimeSpan ttl);
}

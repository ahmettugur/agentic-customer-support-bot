using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Memory;

namespace CustomerSupportBot.Application.Ports.Outbound;

/// <summary>
/// <see cref="CustomerUnderstanding"/>'den ürün önerisi üretir. Kural tabanlıdır — LLM
/// çağırmaz; maliyet sınıfı <c>RecordInteractionAsync</c> ile aynıdır.
///
/// <para>
/// <b>Susma, konuşmak kadar önemli bir sonuçtur.</b> Bu bir destek botu, satış asistanı
/// değil: müşteri şikayet ederken veya yeni bir müşteriyken öneri sunmak agresif satış
/// izlenimi verir. Susma kuralları (duygu, veri yeterliliği) burada — çağıranın her
/// tüketimde tekrar uygulaması gereken bir kontrol değil, servisin garantisi.
/// </para>
/// </summary>
public interface IRecommendationService
{
    /// <summary>
    /// Hiçbir zaman <c>null</c> dönmez ve hiçbir zaman istisna fırlatmaz — susma kuralları
    /// devreye girdiğinde boş liste döner. Çağıran (context provider) bunu ayrıca kontrol
    /// etmek zorunda değildir.
    /// </summary>
    IReadOnlyList<ProductRecommendation> Recommend(AgentSession session, int maxResults = 2);
}

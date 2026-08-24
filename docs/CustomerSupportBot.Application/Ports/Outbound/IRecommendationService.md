# IRecommendationService

**Kaynak:** `Ports/Outbound/IRecommendationService.cs`
**Implementasyon:** [`RecommendationService`](../../Services/Personalization/RecommendationService.md)

## 1. Ne İşe Yarar

`CustomerUnderstanding`'den kural tabanlı ürün önerisi üretir — LLM çağırmaz.

## 2. Hangi Amaçla Kullanılır

Bir context provider (bkz. [ContextProviders.md](../../Providers/ContextProviders.md)) veya
ilgili bir servis, uygun olduğunda önerileri prompt'a/UI'a eklemek için `Recommend`'i çağırır.

## 3. Sorumlulukları

- **Üstlendiği:** Susma kurallarını (duygu durumu, veri yeterliliği) uygulayarak öneri üretmek
  veya bilinçli olarak susmak.
- **Üstlenmediği:** `CustomerUnderstanding`'in sentezlenmesi — o
  [`ICustomerUnderstandingService`](ICustomerUnderstandingService.md)'in işi; bu servis onu
  girdi olarak alır.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Application/Services/Personalization/RecommendationService` implemente eder.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> **Susma, konuşmak kadar önemli bir sonuçtur.** Bu bir destek botu, satış asistanı değil:
> müşteri şikayet ederken veya yeni bir müşteriyken öneri sunmak agresif satış izlenimi
> verir. Susma kuralları burada — çağıranın her tüketimde tekrar uygulaması gereken bir
> kontrol değil, servisin garantisi. `Recommend` hiçbir zaman `null` dönmez ve hiçbir zaman
> istisna fırlatmaz; susma kuralları devreye girdiğinde boş liste döner — çağıran (context
> provider) bunu ayrıca kontrol etmek zorunda değildir.

Kural tabanlı olması (LLM çağırmaması) bilinçlidir: maliyet sınıfı
`RecordInteractionAsync` ile aynı tutulur — her turda ek bir LLM çağrısı yapmaz.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `IReadOnlyList<ProductRecommendation> Recommend(AgentSession session, int maxResults = 2)` | Kural tabanlı öneri üretir; susma kuralları geçerliyse boş liste. |

## 7. Bağımlılıklar

Port arayüzü `CustomerSupportBot.Domain.Model.AgentSession` ve
`CustomerSupportBot.Domain.Model.Memory.ProductRecommendation`'a bağımlıdır.

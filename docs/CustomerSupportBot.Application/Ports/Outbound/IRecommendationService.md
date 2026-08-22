# IRecommendationService

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/IRecommendationService.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound`

## Ne işe yarar?

`IRecommendationService`, <summary> <see cref="CustomerUnderstanding"/>'den ürün önerisi üretir. Kural tabanlıdır — LLM çağırmaz; maliyet sınıfı <c>RecordInteractionAsync</c> ile aynıdır.  <para> <b>Susma, konuşmak kadar önemli bir sonuçtur.</b> Bu bir destek botu, satış asistanı değil: müşteri şikayet ederken veya yeni bir müşteriyken öneri sunmak agresif satış izlenimi verir. Susma kuralları (duygu, veri yeterliliği) burada — çağıranın her tüketimde tekrar uygulaması gereken bir kontrol değil, servisin garantisi. </para> </summary> <summary> Hiçbir zaman <c>null</c> dönmez ve hiçbir zaman istisna fırlatmaz — susma kuralları devreye girdiğinde boş liste döner. Çağıran (context provider) bunu ayrıca kontrol etmek zorunda değildir. </summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IRecommendationService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`

# RecommendationService

- **Kaynak:** `CustomerSupportBot.Application/Services/Personalization/RecommendationService.cs`
- **Tür:** `public sealed class : IRecommendationService`
- **Namespace:** `CustomerSupportBot.Application.Services.Personalization`

## Ne işe yarar?

`RecommendationService`, Application/Services/Personalization/RecommendationService.cs IRecommendationService implementasyonu — kural tabanlı, LLM ÇAĞIRMAZ.  Kural: müşterinin en yeni ürün ilgisiyle AYNI KATEGORİDE, henüz ilgilenmediği, stokta olan ürünleri önerir. Kolaylıkla genişleyebilecek bir yer değil — susma kuralları kadar önemli olan şey, bu servisin ne YAPMADIĞI: işbirlikçi filtreleme yok, satın alma geçmişi analizi yok, LLM'e "bu müşteriye ne satarız" diye sorulmuyor. Veri bunu desteklemiyor (yalnızca bu müşterinin kendi ilgi kaydı var, başka müşterilerle karşılaştırma yok) ve destek bağlamında agresif bir öneri motoru istenmiyor. Bu niyetlerde müşteri zaten aktif bir sorunla/işlemle meşgul — araya ürün önerisi sokmak, "şikayetimi çözmek yerine bana bir şey satmaya çalışıyor" izlenimi verir.

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`RecommendationService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public RecommendationService(ICustomerUnderstandingService understanding,
        IProductCatalogRepository products)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `Recommend`
```csharp
public IReadOnlyList<ProductRecommendation> Recommend(AgentSession session, int maxResults = 2)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IRecommendationService`

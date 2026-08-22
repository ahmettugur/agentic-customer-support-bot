# ProductRecommendationContextProvider

- **Kaynak:** `CustomerSupportBot.Application/Services/Providers/ProductRecommendationContextProvider.cs`
- **Tür:** `public sealed class : IContextProvider`
- **Namespace:** `CustomerSupportBot.Application.Services.Providers`

## Ne işe yarar?

`ProductRecommendationContextProvider`, müşterinin geçmiş siparişleri, şikayet geçmişi ve ilgi duyduğu kategoriler doğrultusunda [IRecommendationService](../Personalization/RecommendationService.md) tarafından üretilen dinamik ürün önerilerini `"## 💡 Önerilen Ürünler"` başlığıyla sistem bağlamına aktaran sağlayıcıdır.

## Hangi amaçla kullanılır`?

- Kullanıcı ürün sorguladığında veya genel tavsiye istediğinde, ajanın müşterinin satın alma geçmişine uygun çapraz satış (cross-sell) ve tamamlayıcı ürün önerileri sunmasını sağlamak.
- Önerilen ürünün adı, fiyatı ve neden önerildiğini (`Reason`) ajana ipucu olarak vermek.

## Constructor ve Başlatma Mantığı

```csharp
public ProductRecommendationContextProvider(
    IRecommendationService recommendationService,
    ILogger<ProductRecommendationContextProvider> logger)
```

### Constructor İçerisinde Yapılan İşler:
- `_recommendationService`: Kural ve geçmiş bazlı öneri motoru.
- `_logger`: Günlükleme motoru.

## Metotlar ve İç Çalışma Mantıkları

### 1. `GetContextAsync`
```csharp
public async Task<string?> GetContextAsync(
    AgentSession session,
    string currentQuery,
    CancellationToken ct = default)
```
- **Ne işe yarar?:** Oturumdaki müşteri için en fazla 3 adet ürün önerisi üretir.
- **İç Mantığı:**
  1. `session.State.AuthenticatedCustomerId` üzerinden `_recommendationService.GetRecommendationsAsync(customerId, limit: 3, ct)` çağrılır.
  2. Öneri listesi boşsa `null` döner.
  3. `StringBuilder` ile ürünlerin adı, birim fiyatı ve öneri gerekçesi formatlanıp döndürülür.

## Özellikler/Properties

- `Name` (`string`): Sabit `"ProductRecommendation"`.
- `Order` (`int`): `8` (Semantik RAG bilgisinden sonra eklenir).

## Bağımlılıklar

- [IContextProvider](IContextProvider.md)
- [IRecommendationService](../Personalization/RecommendationService.md)
- [ProductRecommendation](../../../CustomerSupportBot.Domain/Model/Memory/ProductRecommendation.md)

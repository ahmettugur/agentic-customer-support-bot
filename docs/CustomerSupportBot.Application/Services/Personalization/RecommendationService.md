# RecommendationService

- **Kaynak:** `Services/Personalization/RecommendationService.cs`
- **Tür:** `public sealed class : IRecommendationService`
- **Namespace:** `CustomerSupportBot.Application.Services.Personalization`

## 1. Ne İşe Yarar

Müşteriye en fazla `maxResults` (varsayılan 2) ürün önerisi üretir. **Kural tabanlıdır, LLM
çağırmaz.** Tek kural: müşterinin en son ilgilendiği ürünle **aynı kategoride**, henüz
ilgilenmediği ve stokta olan ürünleri öner.

## 2. Hangi Amaçla Kullanılır

`ProductRecommendationContextProvider` tarafından, sohbet bağlamına "önerilebilecek ürünler"
bloğu eklenmeden önce çağrılır. Amaç, agresif olmayan, veriye dayalı ve **bağlama duyarlı**
(müşteri zaten bir sorunla uğraşıyorsa hiç öneri sunmayan) bir öneri katmanı sağlamak.

## 3. Sorumlulukları

**Üstlendiği:** Dört "susma kuralını" sırayla uygulamak ve kategori eşleşmesi yapmak.

**Bilinçli olarak üstlenmediği:**
- İşbirlikçi filtreleme (collaborative filtering) — başka müşterilerin verisiyle karşılaştırma yok.
- Satın alma geçmişi analizi.
- LLM'e "bu müşteriye ne satarız" diye sormak.

Sebep kaynak yorumunda net: veri bunu desteklemiyor (yalnızca bu müşterinin kendi ilgi kaydı
var) ve destek bağlamında agresif bir öneri motoru istenmiyor.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `ICustomerUnderstandingService.Build(session)` — uzun vadeli sentezlenmiş ilgi listesini alır.
- `IProductCatalogRepository` — kategori/stok bilgisi için katalog sorgusu.
- Tüketicisi: `ProductRecommendationContextProvider` (Services/Providers).
- **Bilerek** `CustomerUnderstandingService`'ten değil `session.State.CurrentIntent`/`Sentiment`'tan
  okur — `CustomerUnderstanding` kasıtlı olarak yalnızca uzun vadeli sentezi taşır, "şu an" bilgisini
  taşımaz (bkz. [CustomerUnderstandingService.md](CustomerUnderstandingService.md)).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Dört susma kuralı, hepsi "yanlış zamanda/yanlış müşteriye satış izlenimi vermeme" ilkesine hizmet eder:

1. **Sentiment negatif/öfkeli ise hiç öneri yok** — destek isteyen birine satış denemesi kötü izlenim bırakır.
2. **Turun niyeti zaten bir sorun/işlem çözme niyetiyse (iade/iptal/şikayet/temsilci) susulur** — ucuz bir kısa devre olarak `CustomerUnderstanding` inşa edilmeden önce kontrol edilir.
3. **Hiç ilgi kaydı yoksa öneri yok** — rastgele ürün önermek "understanding"e dayanmaz, tahmindir.
4. **İlgilenilen ürün kataloğa çözülemiyorsa (silinmiş/değişmiş) veya kategorisizse öneri kurulamaz.**

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Recommend(session, maxResults = 2)` | Yukarıdaki 4 susma kuralını sırayla uygular; geçerse `understanding.ProductInterests[0]` ile aynı kategoride, stokta (`Stock > 0`), henüz bilinmeyen ürünleri `maxResults` kadar döner. Her öneri `"'X' ile aynı kategoride (Y)"` şeklinde bir açıklama taşır. |
| `SilentIntents` *(private static readonly)* | Öneri üretilmeyecek niyetler kümesi: `Complaint`, `ReturnRequest`, `OrderCancellation`, `HumanHandoffRequest`. |

## 7. Bağımlılıklar

Constructor injection ile: `ICustomerUnderstandingService`, `IProductCatalogRepository`.

## Bağlantılar

- [CustomerUnderstandingService.md](CustomerUnderstandingService.md) — girdi verisinin kaynağı
- [../Providers/ProductRecommendationContextProvider.md](../Providers/ProductRecommendationContextProvider.md) — tüketici

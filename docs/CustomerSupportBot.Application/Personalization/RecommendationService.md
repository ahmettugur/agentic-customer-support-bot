# RecommendationService

**Dosya:** `Services/Personalization/RecommendationService.cs`
**Port:** `IRecommendationService`

## 1. Ne İşe Yarar

`CustomerUnderstanding`'den kural tabanlı ürün önerisi üretir. **LLM çağırmaz** — maliyet
sınıfı `RecordInteractionAsync` ile aynıdır.

Bu, Customer Memory işlem hattının **Recommendation** aşamasıdır:

```
Customer → Memory → Understanding → Agents → Recommendation → Action
                                                    ↑ bu servis burada
```

## 2. Bu bir destek botu, satış asistanı değil — susma tasarımın merkezinde

> ⚠️ **Tasarım kararı:** Öneri motoru için en önemli soru "ne önerilir" değil, **"ne zaman
> susulur"**. Şikayet eden veya hakkında hiçbir şey bilmediğimiz bir müşteriye ürün önermek
> agresif satış izlenimi verir — destek bağlamında bunun bedeli, "yardımcı" değil "satıcı"
> gibi görünmektir.

Altı susma kuralı, hepsi boş liste döner (asla `null`, asla exception):

| Kural | Neden |
|---|---|
| Duygu `negative`/`angry` | Şikayet eden birine ürün önermek — en açık agresif-satış hatası. `ICustomerUnderstandingService.Build` bile çağrılmaz (kısa devre). |
| Bu turun niyeti `şikayet`/`iade_talebi`/`sipariş_iptali`/`talep_temsilci` | Müşteri zaten aktif bir sorunla meşgul — araya öneri sokmak "sorunumu çözmek yerine satış yapıyor" izlenimi verir. Duygu kontrolüyle aynı sebepten, `Build()` çağrılmadan **önce** `session.State.CurrentIntent`'ten okunur — `CustomerUnderstanding`'e taşınmaz (bkz. [CustomerUnderstandingService.md](CustomerUnderstandingService.md#4-bilerek-taşımadığı-anlık-niyetduygufaz)). |
| `ProductInterests` boş | Hiçbir ilgi kaydı olmayan bir müşteriye rastgele ürün önermek tahmindir, understanding'e dayanmaz. |
| İlgilenilen ürün kataloğa çözülemiyor | Ürün silinmiş/değişmiş olabilir — sağlam bir temel yok. |
| Aynı kategoride aday yok, ya da tek aday stoksuz | Önerecek gerçekten bir şey yok. |
| Aday zaten bilinen ilgi listesinde | Müşterinin zaten baktığı bir şeyi "yeni öneri" gibi sunmak yanıltıcı. |

## 3. Kural — ne YAPMADIĞI kadar önemli

Müşterinin **en yeni** ürün ilgisiyle (`ProductInterests[0]`) aynı kategoride, henüz
ilgilenmediği, stokta olan ürünleri önerir. Bilerek yapılmayanlar:

- **İşbirlikçi filtreleme yok** — "bu ürünü alanlar şunu da aldı" için başka müşterilerin
  verisi karşılaştırılmıyor; bu veri bu sistemde yok.
- **Satın alma geçmişi analizi yok** — yalnızca `ProductInterests` (soru/ilgi kaydı)
  kullanılıyor, sipariş tablosu değil.
- **LLM'e "bu müşteriye ne satarız" diye sorulmuyor** — kural tabanlı, açıklanabilir,
  test edilebilir bir eşleşme.

Bunlar eksiklik değil, kapsam kararı: mevcut veri (tek müşterinin kendi ilgi kaydı) bu
karmaşıklığı desteklemiyor ve destek bağlamında agresif bir öneri motoru istenmiyor.

## 4. Nerede görünür

`ProductRecommendationContextProvider` (Order 8 — SemanticMemory'den sonra, CustomerContext'ten
önce; bütçe dolarsa önce bu düşer) önerileri bağlama ekler. Metin açıkça **talimat değil
öneri** olarak etiketlenir: *"isteğe bağlı, YALNIZCA uygun bağlamda bahset — zorlama"* —
model hiçbir tool'u otomatik tetiklemez, bir eylemi otonom başlatmaz.

## Bağlantılar

- [CustomerUnderstandingService.md](CustomerUnderstandingService.md) — Girdi kaynağı
- [../Providers/ContextProviders.md](../Providers/ContextProviders.md) — `ProductRecommendationContextProvider`
- [../Ports.md](../Ports.md) — Port kaydı

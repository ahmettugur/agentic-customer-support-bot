# CustomerProfileEntity

**Dosya:** `EfCore/Entities/Personalization/CustomerProfileEntity.cs`
**Şema/Tablo:** `personalization.customer_profiles`
**Configuration:** [CustomerProfileConfiguration](../../Configurations/Personalization/CustomerProfileConfiguration.md)

## 1. Ne İşe Yarar

Bir müşterinin uzun ömürlü (oturumlar arası kalıcı) kişiselleştirme profilini temsil eder —
tercih ettiği dil/ton, sık sorduğu konular, ürün ilgi alanları, geçmiş değerlendirmeleri ve
LLM-türetilmiş davranışsal çıkarımlar.

## 2. Hangi Amaçla Kullanılır

`CustomerProfileContextProvider` (Application katmanı) sohbet başında bu profili okuyup
bağlama enjekte eder — böylece bot, müşteriyi önceki oturumlardan "hatırlıyormuş" gibi
davranabilir (ör. tercih edilen dil, daha önce ilgilendiği ürün kategorisi). Improvement/
Personalization servisleri her oturum sonunda bu profili günceller (`Consolidate` benzeri bir
işlemle).

## 3. Sorumlulukları

- **Üstlendiği:** Müşterinin kalıcı tercihlerini ve LLM-türetilmiş çıkarımlarını taşımak.
- **Üstlenmediği:** Bu çıkarımların nasıl üretildiği (LLM analizi, sinyal toplama) — bu,
  Personalization servisinin işi.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`CustomerId` (PK), `CustomerEntity.Id`'ye (Catalog şeması) mantıksal referans verir (gerçek FK
yok). `RecentRatingsJson`, [RatingEntity](../Analytics/RatingEntity.md) kayıtlarının bir
özetini/kopyasını tutar (denormalize edilmiş — performans için, RatingEntity'ye her seferinde
join yapmak yerine).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Neredeyse tüm koleksiyon alanları (`IntentFrequencyJson`, `ProductInterestsJson`,
`RecentRatingsJson`, `TraitsJson`) `jsonb` olarak saklanır — bunlar müşteriye özgü, değişken
boyutlu listelerdir (bir müşterinin kaç farklı niyet/ilgi alanı olacağı önceden bilinmez);
ayrı tablolara normalize etmek yerine profil tek satırda tutulur çünkü bu veri her zaman
"müşteri profilinin tamamı" olarak birlikte okunur, parça parça sorgulanmaz.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `CustomerId` | `string` | Birincil anahtar. |
| `PreferredLanguage` | `string` | Tercih edilen dil, varsayılan `"tr"`. |
| `PreferredTone` | `string` | Tercih edilen üslup, varsayılan `"neutral"`. |
| `IntentFrequencyJson` | `string` | Niyet başına geçmiş frekans, `jsonb` (varsayılan `{}`). |
| `ProductInterestsJson` | `string` | İlgilenilen ürün/kategori listesi, `jsonb` dizi. |
| `RecentRatingsJson` | `string` | Son değerlendirmelerin özeti, `jsonb` dizi. |
| `TraitsJson` | `string` | LLM-türetilmiş davranışsal çıkarımlar, `jsonb` dizi. |
| `Summary` | `string?` | Profilin LLM-üretilmiş serbest metin özeti. |
| `AdminNote` | `string?` | Admin tarafından elle eklenmiş not. |
| `TotalSessions` | `int` | Toplam oturum sayısı. |
| `TotalTurns` | `int` | Toplam tur sayısı. |
| `CreatedAt` | `DateTime` | Profilin ilk oluşturulma zamanı. |
| `LastInteractionAt` | `DateTime` | Son etkileşim zamanı. |
| `LastConsolidatedAt` | `DateTime?` | Profilin en son ne zaman yeniden hesaplandığı. |

## 7. Bağımlılıklar

Yok — saf veri sınıfı.

## Bağlantılar

- [CustomerProfileConfiguration](../../Configurations/Personalization/CustomerProfileConfiguration.md)
- [RatingEntity](../Analytics/RatingEntity.md)
- [README](../README.md)

# CustomerProfile

- **Kaynak:** `CustomerSupportBot.Domain/Model/Memory/CustomerProfile.cs`
- **Tür:** `public sealed class` (mutable)
- **Namespace:** `CustomerSupportBot.Domain.Model.Memory`

## 1. Ne İşe Yarar

Bir müşterinin geçmiş etkileşimlerinden çıkarılmış **uzun ömürlü profilini** temsil eder — dil
tercihi, üslup, ilgi alanları, geçmiş puanlar ve LLM tarafından üretilmiş özet/çıkarımlar.

## 2. Hangi Amaçla Kullanılır

Her oturum sonunda heuristik olarak güncellenir (sayaçlar, ilgi alanları). Admin panelinden
tetiklendiğinde LLM ile "consolidate" edilip `Summary`/`PreferredTone`/`Traits` alanları
zenginleştirilir. Kullanıcı 4+ haneli bir müşteri ID ile yeni bir oturum açtığında,
`CustomerProfileContextProvider` (Application katmanı) bu profili okuyup context'e enjekte
ederek ajanların proaktif/kişiselleştirilmiş davranmasını sağlar (ör. "Bu müşteri kısa ve
teknik dil tercih ediyor", "Geçmişte iki kez Dell XPS 15 sipariş etti").

## 3. Sorumlulukları

- ✅ Müşteri hakkında biriken heuristik ve LLM-türetilmiş bilgiyi taşımak
- ✅ Context'e enjekte edilecek kısa bir metin özeti üretmek (`ToContextLine`, fallback amaçlı)
- ❌ LLM ile "consolidate" işlemini yürütmek — bu ayrı bir Application servisinin işi
- ❌ Context enjeksiyon formatını belirlemek — asıl format `CustomerProfileContextProvider`'da,
  `ToContextLine` sadece fallback'tir

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Kim üretir/günceller:** Oturum sonu heuristik güncelleme akışı + admin tetiklemeli LLM
  consolidate servisi (Application katmanı)
- **Kim tüketir:** `CustomerProfileContextProvider` (`ContextPipeline`'a bağlı provider'lardan biri)
- **İçerdiği tip:** [InferredTrait](InferredTrait.md) — `Traits` listesinin elemanı

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 💡 **`Summary` (olgu) ile `Traits` (yorum) kasıtlı olarak ayrı tutulur.** `Summary` LLM'in
> ürettiği kısa bir özet metnidir; `Traits` ise her biri kendi `Confidence`/`Source`'unu taşıyan,
> yanlış çıkabilecek çıkarımlardır (bkz. [InferredTrait](InferredTrait.md)). Bu ayrım olmadan bir
> tahmin, olgu gibi sunulmuş olurdu.

> 💡 **`Traits` her consolidate çağrısında baştan üretilir, birikmez** — eski çıkarımların
> güncel olmayan bir müşteri davranışını süresiz taşımasını önlemek için.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `CustomerId` | `string` | Müşteri kimliği (ör. "1027") — primary key |
| `PreferredLanguage` | `string` | ISO 639-1 dil kodu, varsayılan `"tr"` |
| `PreferredTone` | `string` | `"formal"` \| `"casual"` \| `"concise"` \| `"verbose"` — LLM consolidate ile dolar |
| `IntentFrequency` | `Dictionary<string, int>` | Niyet → frekans (ör. `{"order_status": 5}`) |
| `ProductInterests` | `List<string>` | İlgilenilen ürünler (en yenisi başta, max 10) |
| `RecentRatings` | `List<int>` | Son N oturumdan toplanan rating'ler (max 10) |
| `Summary` | `string?` | LLM tarafından üretilmiş 1-2 cümlelik özet |
| `Traits` | `List<InferredTrait>` | Çıkarılmış iddialar — bkz. [InferredTrait](InferredTrait.md) |
| `AdminNote` | `string?` | Admin'in elle eklediği serbest not (override) |
| `TotalSessions` | `int` | Toplam oturum sayısı |
| `TotalTurns` | `int` | Toplam tur sayısı (kullanıcı mesajı) |
| `CreatedAt` | `DateTime` | Oluşturulma anı (UTC) |
| `LastInteractionAt` | `DateTime` | Son etkileşim anı (UTC) |
| `LastConsolidatedAt` | `DateTime?` | Son LLM consolidate anı |
| `ToContextLine()` | `string` | Fallback context metni — `Summary` + ortalama rating + ilk 3 ilgi alanı |

## 7. Bağımlılıklar

Yok — saf domain modeli, dış bağımlılığı yok.

## Bağlantılar

- [InferredTrait.md](InferredTrait.md) — `Traits` listesinin elemanı
- [CustomerUnderstanding.md](CustomerUnderstanding.md) — İlişkili müşteri anlama modeli

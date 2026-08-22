# CustomerUnderstandingService

- **Kaynak:** `Services/Personalization/CustomerUnderstandingService.cs`
- **Tür:** `public sealed class : ICustomerUnderstandingService`
- **Namespace:** `CustomerSupportBot.Application.Services.Personalization`

## 1. Ne İşe Yarar

`CustomerProfile`'ı (ham, biriken sayaçlar) okunabilir bir `CustomerUnderstanding` DTO'suna
dönüştürür — üst sınırlarla kırpılmış (en fazla 5 ürün ilgisi, en fazla 3 top niyet), prompt'a
veya UI'a doğrudan gösterilebilecek bir "özet görünüm". **LLM çağırmaz** — saf, senkron bir
projeksiyon/dönüşümdür; maliyet sınıfı `RecordInteractionAsync` ile aynıdır (sıfır).

## 2. Hangi Amaçla Kullanılır

[`RecommendationService`](RecommendationService.md) ve `CustomerProfileContextProvider` gibi
tüketiciler, ham `CustomerProfile`'ın büyük/karmaşık iç yapısıyla (tüm `IntentFrequency`
sözlüğü, sınırsız `ProductInterests` vb.) uğraşmak yerine bu servisin ürettiği kırpılmış,
sıralanmış görünümü kullanır.

## 3. Sorumlulukları

**Üstlendiği:** `CustomerProfile`'dan `CustomerUnderstanding`'e alan-alan dönüşüm; en sık 3
niyeti ve en yeni 5 ürün ilgisini seçmek; ortalama rating hesaplamak.

**Bilinçli olarak taşımadığı:** "Şu an" bilgisi — yani turun mevcut niyeti (`CurrentIntent`)
veya mevcut sentiment. `CustomerUnderstanding` yalnızca **uzun vadeli, birikmiş** sentezi
taşır. Bu ayrım [`RecommendationService`](RecommendationService.md)'in susma kurallarının
neden `session.State`'ten (anlık) okuyup `CustomerUnderstanding`'den (birikmiş) okumadığını
açıklar.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `ICustomerProfileStore` — tek bağımlılık, ham profili okumak için.
- Tüketicileri: [`RecommendationService`](RecommendationService.md), `CustomerProfileContextProvider`.
- Girdisi [`CustomerProfileService`](CustomerProfileService.md) tarafından doldurulan `CustomerProfile`'dır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Ayrı bir "understanding" katmanı olmasının sebebi: ham profil (`CustomerProfile`) yazma
tarafının optimize edildiği bir veri yapısıdır (sözlükler, sınırsız listeler); okuma tarafının
ihtiyacı ise sıralı, kırpılmış, sunuma hazır bir görünümdür. İkisini aynı tipte tutmak yazma
tarafını okuma ihtiyaçlarına göre şişirirdi. `Build` metodunun `AgentSession` alması (ham
`customerId` string'i değil), `session.State.AuthenticatedCustomerId`'nin JWT'den doğrulanmış
kimlik olduğunu garanti eder — sahte/tahmini bir customerId ile profil sorgulanamaz.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Build(session)` | `session.State.AuthenticatedCustomerId` boşsa veya `_store`'da profil yoksa veya `TotalTurns == 0` ise `null` döner (henüz sentezlenecek veri yok). Aksi halde `CustomerUnderstanding` kaydını doldurur: `Persona=profile.Summary`, en sık 3 niyet (`TopIntents`), en yeni 5 ürün ilgisi (`ProductInterests`), ortalama rating (`RecentRatings.Average()`, hiç puan yoksa `null`). |
| `MaxProductInterestsShown = 5`, `MaxTopIntentsShown = 3` *(private const)* | Çıktının boyutunu sınırlayan sabitler. |

## 7. Bağımlılıklar

Constructor injection ile: `ICustomerProfileStore`.

## Bağlantılar

- [CustomerProfileService.md](CustomerProfileService.md) — girdi verisinin (`CustomerProfile`) üreticisi
- [RecommendationService.md](RecommendationService.md) — bir tüketici

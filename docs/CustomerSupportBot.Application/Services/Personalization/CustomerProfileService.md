# CustomerProfileService

- **Kaynak:** `Services/Personalization/CustomerProfileService.cs`
- **Tür:** `public sealed partial class : ICustomerProfileService`
- **Namespace:** `CustomerSupportBot.Application.Services.Personalization`

## 1. Ne İşe Yarar

Müşteri bazlı kişiselleştirme profilini (`CustomerProfile`) günceller. İki tamamen farklı
maliyet sınıfında çalışan güncelleme yolu vardır ve bu ayrım kasıtlıdır:

| Yol | Ne zaman çağrılır | Maliyet |
|---|---|---|
| `RecordInteractionAsync` | Her workflow turunun sonunda, otomatik | **Sıfır LLM** — saf kural tabanlı |
| `ConsolidateAsync` | Admin elle tetikler (ya da N turda bir bir job) | **1 LLM çağrısı** |

## 2. Hangi Amaçla Kullanılır

`RecordInteractionAsync`, her turda niyet frekansını, bahsedilen ürünleri, dil tercihini ve
son puanlamayı **deterministik** biçimde biriktirir — bu, `CustomerProfileContextProvider`'ın
bir sonraki turda LLM'e enjekte edeceği ham verinin kaynağıdır. `ConsolidateAsync` ise bu
biriken ham veriyi LLM'e göstererek kısa bir "Summary" ve önerilen bir "PreferredTone" +
en fazla 5 `InferredTrait` üretir — sık çağrılmaz, gerçek zamanlı akışın bir parçası değildir.

## 3. Sorumlulukları

**Üstlendiği:**
- Profildeki sayaçları (`IntentFrequency`, `ProductInterests`, `RecentRatings`) her turda
  güncellemek, üst sınırlarını (`MaxIntents=20`, `MaxProductInterests=10`, `MaxRecentRatings=10`)
  korumak.
- Basit karakter/kelime tabanlı dil tespiti (`LooksTurkish`/`LooksEnglish`).
- LLM'den gelen `traits` JSON dizisini savunmacı biçimde ayrıştırmak (`ParseTraits`) — eksik
  `claim`, aralık dışı `confidence`, string olarak gelen sayı gibi durumlara dayanıklı.
- Per-customer eşzamanlılığı `IAppDistributedLock` ile (`profile:{customerId}` anahtarıyla)
  serialize etmek.

**Üstlenmediği:**
- Profilin nasıl saklandığı (`ICustomerProfileStore`'un işi — Redis/Postgres detayına bu
  sınıf hiç bakmaz).
- Profilin prompt'a nasıl enjekte edileceği ([`CustomerProfileContextProvider`](../Providers/CustomerProfileContextProvider.md)'ın işi).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `ICustomerProfileStore` (Outbound port) — profilin okunduğu/yazıldığı yer.
- `IGeneralChatClient` — `ConsolidateAsync` içindeki tek LLM çağrısı.
- `IAppDistributedLock` — çoklu pod'da aynı müşteri için yarış durumunu engeller.
- `IProductCatalogRepository` — `ExtractProductMentions` metin içinde geçen ürün adlarını
  katalogla eşleştirmek için kullanır.
- Tüketicisi: [`PersonalizationPortService`](PersonalizationPortService.md) (Inbound port
  implementasyonu, `ChatPortService`'in turdan sonra çağırdığı yer) ve
  [`CustomerProfileContextProvider`](../Providers/CustomerProfileContextProvider.md) (okuma tarafı).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> **Traits birikmez — her `ConsolidateAsync` çağrısında baştan üretilir.**

Bilinçli bir tasarım kararı: profil artımlı bir "delta" değil, biriken ham sayaçlardan
(`IntentFrequency`, `ProductInterests`, `RecentRatings`) oluşuyor — `BuildConsolidatePrompt`
her seferinde TÜM birikmiş veriyi LLM'e gösteriyor. Trait'ler biriktirilseydi, örneğin 6 ay
önce doğru olup artık geçersiz olan bir gözlem ("yeni müşteri, az veri var") sonsuza kadar
profilde kalır ve yenileriyle çelişirdi. Bunun bedeli: iki `ConsolidateAsync` çağrısı arasında
trait sayısı hiç artmaz, önceki confidence'lar önemsiz hale gelir — kabul edilen bir maliyet.

Deterministik/LLM ayrımı da bilinçli: episodik bellek yazımı (bkz. Memory servisleri) zaten
her turda LLM çağırmıyor, profil de aynı maliyet sınıfında kalmalı — aksi halde her mesajda
ekstra bir LLM round-trip'i (gecikme + maliyet) eklenmiş olurdu.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `RecordInteractionAsync(customerId, userQuery, botResponse, intent, rating?, isNewSession, ct)` | Distributed lock altında `RecordInteractionCore`'u çalıştırır. `customerId` veya `userQuery` boşsa sessizce `null` döner (kişiselleştirme "iyileştirme", zorunluluk değil). |
| `RecordInteractionCore(...)` *(private)* | Asıl kural tabanlı güncelleme: tur/oturum sayaçlarını artırır, niyet frekansını günceller (kapasite aşılırsa en zayıf niyeti atar), `ExtractProductMentions` ile bahsedilen ürünleri en yeni-önde listeye ekler, dili tespit eder, rating'i son-N listesine ekler. |
| `ConsolidateAsync(customerId, ct)` | Profildeki ham veriyi `BuildConsolidatePrompt` ile prompt'a çevirir, LLM'e SADECE-JSON isteği gönderir, `Summary`/`PreferredTone`/`Traits` alanlarını günceller. `TotalTurns == 0` ise hiç LLM çağırmadan profili aynen döner (boş veriden özet üretmenin anlamı yok). Hata durumunda (`catch`) LLM çağrısı/parse başarısız olsa bile eski profili döner — consolidate başarısızlığı profilin kaybolmasına yol açmaz. |
| `ParseTraits(root, customerId, totalTurns)` *(internal static)* | LLM'in `traits` JSON dizisini `InferredTrait` listesine çevirir; `confidence`'ı `[0,1]`'e kırpar, eksik/boş `claim`'i atlar, `MaxTraits=5` ile sınırlar. |
| `ExtractJson(text)` *(internal static)* | LLM çıktısından JSON'u çıkarır — önce ```` ```json ``` ```` fence'ini dener, yoksa ilk `{` ile son `}` arasını alır. |
| `ExtractProductMentions(text)` *(internal)* | Metinde katalogdaki ürün adlarının (case-insensitive) geçip geçmediğini tarar. |
| `LooksTurkish(text)` / `LooksEnglish(text)` *(internal static)* | Türkçe özel karakter (`çğıöşü`) veya yaygın Türkçe/İngilizce kelime regex'iyle basit dil tahmini. |

## 7. Bağımlılıklar

Constructor injection ile: `ICustomerProfileStore`, `IGeneralChatClient`, `IAppDistributedLock`,
`IProductCatalogRepository`, `ILogger<CustomerProfileService>`.

## Bağlantılar

- [PersonalizationPortService.md](PersonalizationPortService.md) — bu servisi çağıran port implementasyonu
- [../Providers/CustomerProfileContextProvider.md](../Providers/CustomerProfileContextProvider.md) — profili prompt'a enjekte eden tüketici
- [CustomerUnderstandingService.md](CustomerUnderstandingService.md) — ilişkili, ama LLM-türetilmiş daha derin çıkarım yapan başka bir servis

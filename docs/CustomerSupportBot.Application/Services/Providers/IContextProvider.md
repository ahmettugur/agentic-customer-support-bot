# IContextProvider

- **Kaynak:** `Services/Providers/IContextProvider.cs`
- **Tür:** `public interface`
- **Namespace:** `CustomerSupportBot.Application.Services.Providers`

## 1. Ne İşe Yarar

[`ContextPipeline`](../Chat/ContextPipeline.md) boru hattında paralel çalışan tüm dinamik
bağlam sağlayıcıların (konuşma özeti, müşteri profili, RAG semantik bellek, ürün önerisi)
uyması gereken standart sözleşmedir.

## 2. Hangi Amaçla Kullanılır

Çoklu ajan iş akışında prompt'a eklenecek farklı bağlam kaynaklarını modüler, sıralı (`Order`)
ve birbirinden izole (bir tanesi patlarsa diğerleri etkilenmez) biçimde çalıştırmak için.

## 3. Sorumlulukları

Her implementasyonun üstlendiği: kendi `Name`/`Order`/`IsCritical` değerlerini bildirmek ve
`GetContextAsync` ile bağlam üretmek (veya katkısı yoksa `null` dönmek).

**Üstlenmediği** (pipeline'ın işi): zamanlama, hata izolasyonu, bütçe/kırpma, kritik-provider
düştüğünde uyarı enjeksiyonu — bunların hiçbiri implementasyonların sorumluluğunda değil,
[`ContextPipeline`](../Chat/ContextPipeline.md) merkezi olarak uygular.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- [`ContextPipeline`](../Chat/ContextPipeline.md) — tüm implementasyonları DI'dan toplayıp
  `Order`'a göre paralel çalıştıran tüketici.
- Bilinen implementasyonlar: `ConversationSummaryProvider`, `CustomerProfileContextProvider`,
  `SemanticMemoryContextProvider`, `ProductRecommendationContextProvider`, `NoopContextProvider`.
- ⚠️ Geçmişte bir `CustomerContextProvider` implementasyonu vardı (`IsCritical=true`, müşterinin
  sipariş/şikayet geçmişini koşulsuz enjekte ediyordu) — işlevi zaten var olan sorgu tool'larıyla
  (`get_last_order`, `get_all_orders`, `complaint_status` vb.) tam çakıştığı için **tamamen
  kaldırıldı**. Şu an hiçbir implementasyon `IsCritical=true` değil — bkz. madde 5.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

### `IsCritical` — kritik/iyileştirici ayrımı

Bu provider'ın katkısı **doğru cevap için gerekli mi**, yoksa yalnızca **iyileştirici mi**?
Ayrım, hata anında ne yapılacağını belirler:

- **İyileştirici (varsayılan, `false`):** provider düşerse (ör. semantik bellek erişilemiyor)
  bot yine makul bir cevap verebilir — sessizce atlanır.
- **Kritik (`true`):** provider düşerse model eksikliği fark etmez ve büyük olasılıkla
  "kayıtlı siparişiniz bulunamadı" gibi **yanlış bir olgu** üretir (altyapı hatası → uydurma
  cevap). Bu yüzden kritik bir provider düştüğünde pipeline sessiz kalmaz — bağlama açık bir
  "bu bilgi şu an okunamıyor, tahminde bulunma" uyarısı ekler.

### `currentQuery` neden ayrı bir parametre

Kullanıcının bu turdaki mesajı `session`'ın geçmişinden okunamaz, çünkü geçmiş
(`AddExchange`/`PersistExchange`) workflow **bittikten sonra** yazılır — bağlam kurulurken
güncel mesaj henüz history'de yoktur.

> 🐞 **Geçmişte gerekliydi:** Bu parametre yokken `SemanticMemoryContextProvider` aranacak
> metni geçmişteki son kullanıcı mesajından tahmin ediyordu; sonuç olarak ilk turda hiç
> retrieval yapılmıyor, sonraki turlarda ise arama **bir önceki** turun sorusuyla yapılıyordu —
> hem bilgi tabanı hem onaylanmış dersler yanlış sorguyla getiriliyordu.

### `ct` neden zorunlu geçilmeli

Provider başına zaman aşımı ve çağıran iptalinin birleşimidir. Ağ/LLM çağrısı yapan her
provider bunu aşağıya geçirmelidir — aksi halde `ContextPipeline`'ın süresi dolsa bile iş
arka planda sürmeye devam eder (bkz. [ContextPipeline.md §3.1](../Chat/ContextPipeline.md)).

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Name` (`string`, get) | Provider'ın tekil adı (ör. `"ConversationSummary"`, `"SemanticMemory"`) — `ContextResult` raporlamasında kullanılır. |
| `Order` (`int`, get) | Boru hattındaki birleştirme sırası; küçük sayı önce yerleşir. Bütçe dolduğunda en yüksek `Order`'lı provider'lar önce düşürülür. |
| `IsCritical` (`bool`, get, varsayılan `false`) | Yukarıya bakınız — kritik/iyileştirici ayrımı. |
| `GetContextAsync(session, currentQuery, ct)` | Verilen oturum ve bu turun sorgusu için bağlam metni üretir; eklenecek bir şey yoksa `null` döner. |

## 7. Bağımlılıklar

Arayüz olduğu için kendi bağımlılığı yok; implementasyonlar kendi bağımlılıklarını taşır
(ör. `SemanticMemoryContextProvider` bir vektör deposuna, `ConversationSummaryProvider` bir
`IChatClient`'a bağımlıdır).

## Bağlantılar

- [../Chat/ContextPipeline.md](../Chat/ContextPipeline.md) — tüketici ve orkestrasyon detayları
- [CustomerProfileContextProvider.md](CustomerProfileContextProvider.md), [SemanticMemoryContextProvider.md](SemanticMemoryContextProvider.md), [ConversationSummaryProvider.md](ConversationSummaryProvider.md), [ProductRecommendationContextProvider.md](ProductRecommendationContextProvider.md), [NoopContextProvider.md](NoopContextProvider.md), [CustomerIdentityHintBuilder.md](CustomerIdentityHintBuilder.md) — implementasyonlar

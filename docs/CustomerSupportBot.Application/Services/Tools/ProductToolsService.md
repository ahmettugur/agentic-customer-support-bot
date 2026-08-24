# ProductToolsService

- **Kaynak:** `Services/Tools/ProductToolsService.cs`
- **Tür:** `public sealed class : IProductToolsService`
- **Namespace:** `CustomerSupportBot.Application.Services.Tools`

## 1. Ne İşe Yarar

Ürün sorgulama araçları: tekil ürün sorgusu (`ProductInquiryTool`) ve kategoriye göre listeleme
(`ProductListTool`, kategori verilmezse bir "kategori seçim ekranı" ipucu yayınlar). Bu iki
tool salt-okunurdur — HITL onayı gerektirmez.

## 2. Hangi Amaçla Kullanılır

`ProductAgent` (Adapters.Agents) tarafından ürünle ilgili sorulara cevap vermek için
kullanılır; `ProductListTool`, kullanıcı arayüzüne (Blazor) `IUiHintEmitter` üzerinden
tıklanabilir bir kategori seçim ekranı ipucu da gönderebilir.

## 3. Sorumlulukları

**Üstlendiği:** Ürün/kategori sorgusu, fiyat biçimlendirme (tr-TR kültürü, sabit "TL" birimi),
kategori seçim ekranı ipucu yayınlama ve bu ipucunun **gerçekte** ekrana ulaşıp ulaşmadığına
göre farklı bir LLM talimatı üretmek.

**Üstlenmediği:** Ürün kataloğunun kalıcılığı (`IProductCatalogRepository`'nin işi), UI
ipucunun gerçek iletimi (`IUiHintEmitter`'ın işi).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IProductCatalogRepository` — ürün/kategori sorguları.
- `IUiHintEmitter` — kategori seçim ekranı ipucunu (varsa) canlı bağlantıya yayınlar.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

### `FormatPrice` neden kültürü açıkça `tr-TR` verir

Ortamın `CurrentCulture`'ına bırakılmaz — sunucu kültürü ortama göre değişir (container'larda
genelde invariant) ve aynı fiyat bir yerde `18,00`, başka yerde `18.00` olarak çıkardı. Sembol
(`₺`) yerine `"TL"` yazılır: bu metin hem LLM'e hem de sesli kanalda TTS'e gidiyor, `"TL"` her
ikisinde de tek anlama gelir (₺ sembolü TTS'te garip okunabilir).

### `ProductListTool`'da kategori bulunamama ile kategori-boş-olma ayrımı

- Kategori adı **hiç yoksa** (`!result.CategoryExists`): geçerli kategori listesi döndürülür —
  LLM geçerli adlarla tekrar deneyebilsin diye.
- Kategori **var ama ürünsüz**: aynı adla tekrar denemek anlamsız olduğu açıkça söylenir,
  LLM'e "başka bir kategori öner" talimatı verilir.

Bu ayrım, LLM'in aynı hatalı isteği tekrar tekrar denemesini (ping-pong) önler.

### `ShowCategoryPicker` — ipucu her zaman ekrana ulaşmaz

> 🐞 **Kritik nokta:** Sesli (native realtime) kanalda ambient session bağlamı kurulmadığı
> için `IUiHintEmitter.Emit` ipucunu düşürür ve kullanıcının bakacağı bir ekran yoktur. Eskiden
> bu durumda da LLM'e "kategori seçim ekranı gösterildi" deniyordu; model de kullanıcıyı
> **olmayan** bir ekrana yönlendiriyordu. Aynı şey katalogda listelenebilir kategori
> olmadığında da oluyordu: arayüz boş listeyi hiç çizmiyor, model ise seçim bekliyordu.
> `IUiHintEmitter.Emit`'in dönüş değeri (`shown: bool`) artık kontrol edilir — ipucu
> **gerçekten** ekrana ulaştıysa model "kullanıcı seçecek" der, ulaşmadıysa (sesli kanal)
> kategorileri kendisi sesli olarak okur.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `ProductInquiryTool(productName)` | Ürün adı/kısmi adıyla arama; tam eşleşme `confidence=1.0`, kısmi eşleşme `confidence=0.85`; bulunamazsa `ProductNotFound`. |
| `ProductListTool(category?)` | Kategori boşsa `ShowCategoryPicker()`; kategori mevcut değilse geçerli kategori listesiyle hata; kategori var ama boşsa uyarı; aksi halde ürünleri fiyat/stok bilgisiyle listeler (kanonik kategori adı kullanılır — çağıranın yazdığı ham metin değil). |
| `ShowCategoryPicker()` *(private)* | Seçilebilir kategori yoksa hata; varsa `IUiHintEmitter.Emit` ile `category_picker` ipucu yayınlar ve **gerçekten gösterilip gösterilmediğine** göre farklı bir LLM talimatı üretir (bkz. madde 5). |
| `FormatPrice(price)` *(private static)* | `"18,00 TL"` biçiminde, sabit tr-TR kültürüyle. |

## 7. Bağımlılıklar

Constructor injection ile: `IProductCatalogRepository`, `IUiHintEmitter`.

## Bağlantılar

- [../UiHint/UiHintEmitter.md](../UiHint/UiHintEmitter.md) — ipucu yayın mekanizması

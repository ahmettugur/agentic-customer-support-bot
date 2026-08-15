# ProductToolsService

**Dosya:** `Services/Tools/ProductToolsService.cs`

## 1. Ne İşe Yarar

Ürün domain tool'larını implement eder — ürün arama (`ProductInquiryTool`) ve katalog
listeleme / kategori seçimi (`ProductListTool`).

## 2. Hangi Amaçla Kullanılır

`ProductAgent` bu tool'ları çağırır. Salt-okunur oldukları için HITL onayı gerektirmezler ve
sesli (native realtime) kanalda da çağrılabilirler — aşağıdaki kanal farkı tam olarak bu
yüzden önemli.

## 3. `ProductListTool` — iki dal

```
category boş?  ──evet──►  ShowCategoryPicker()
     │hayır
     ▼
GetByCategory(category)
     ├─ kategori YOK    → CATEGORY_NOT_FOUND  + geçerli kategori listesi
     ├─ kategori BOŞ    → PRODUCT_NOT_FOUND   + "tekrar deneme" talimatı
     └─ ürün var        → Ok + kanonik kategori adı
```

> ⏱️ **Zamanlama:** İpucu tool çalışırken yayınlanır, yani kategori listesi tur bitmeden
> tarayıcıya ulaşır. Arayüz veriyi hemen alır ama kartları **tur bitene kadar çizmez** —
> sebebi ve düzeltilen hata için bkz.
> [`Chat.md`](../../CustomerSupportBot.Web/Pages/Chat.md#kategori-seçim-kartları--veri-erken-gelir-gösterim-ertelenir).

### 3.1 Picker gerçekten gösterildi mi? (`ShowCategoryPicker`)

`IUiHintEmitter.Emit` artık **`bool` döner**: ipucu kuyruğa girdiyse `true`, ambient session
bağlamı olmadığı için düştüyse `false`. `ProductListTool` mesajını bu sonuca göre kurar.

> 🐞 **Bulundu ve düzeltildi — doğrulanmamış "gösterildi" iddiası.** `Emit` eskiden `void`'di
> ve `UiHintEmitter` session yoksa sessizce çıkıyordu
> (`if (string.IsNullOrEmpty(sessionId)) return;`). Tool ise her hâlükârda LLM'e
> *"Kategori seçim ekranı kullanıcıya gösterildi"* diyordu.
>
> Bu yol teorik değil: `product_list_tool` sesli tool setinde tanımlı
> (`RealtimeFunctionTools.cs`) ve `RealtimeNativeService` içinde **hiç `SetScope` çağrısı
> yok** — yani native sesli modda ambient bağlam kurulmaz, ipucu düşer, kullanıcının
> bakacağı bir ekran olmaz. Model yine de kullanıcıyı olmayan bir ekrana yönlendiriyordu.
>
> Artık ipucu düştüğünde tool, modele kategorileri **kendisinin okumasını** söyler ve
> `data.pickerShown = false` döner.

Ayrıca listelenebilir kategori hiç yoksa picker gösterilmeye çalışılmaz: arayüz boş listeyi
zaten çizmediği için (`@if (msg.CategoryPicker is { Count: > 0 })`) kullanıcı boş ekrana
bakarken model seçim bekliyordu — kilitlenme. Bu durumda `PRODUCT_NOT_FOUND` döner.

### 3.2 "Kategori yok" ile "kategori boş" ayrımı

> 🐞 **Bulundu ve düzeltildi — iki farklı durum tek hataya iniyordu.** `GetByCategory` hem
> "böyle bir kategori yok" hem "kategori var ama içi boş" için `[]` dönüyordu; tool ikisini
> de `PRODUCT_NOT_FOUND` + *"'X' kategorisinde ürün bulunmamaktadır."* diye raporluyordu.
>
> Model için bunlar zıt durumlar: ad yanlışsa düzeltip tekrar denemeli, kategori gerçekten
> boşsa denemek anlamsız. Üstelik mesajda geçerli kategori listesi de yoktu, yani model
> kendini düzeltemiyordu.
>
> Repo artık [`CategoryProducts`](../../CustomerSupportBot.Domain/Model/CategoryProducts.md)
> döner; tool da iki durumu ayrı hata koduna ve ayrı talimata bağlar.

Canlı veride ikisi de mevcut — seed'de 4 kategori (`Çorbalar`, `Tahıllar`,
`Tahıl Gevrekleri`, `Cips & Atıştrmalıklar`) gerçekten boştur.

### 3.3 Boş kategoriler artık seçenek olarak sunulmuyor

`GetCategories()` → **`GetSelectableCategories()`** olarak yeniden adlandırıldı ve yalnızca en
az bir ürünü olan kategorileri döner. Eski hâlinde picker 21 kategori gösteriyor, kullanıcı o
4 boş kategoriden birine tıklayınca "ürün bulunmamaktadır" alıyordu — seçim ekranının
seçilemeyen seçenek sunması. Ad, sözleşmenin kendisini anlatsın diye değiştirildi (tek
çağıran bu tool'du).

### 3.4 Kanonik ad echo'su

Başarılı yanıtta `data.category` artık çağıranın yazdığı değil **katalogdaki kanonik ad**.
Kolon `und-u-ks-level1` collation'lı olduğu için `"içecekler"` de eşleşir; eskiden aynı
payload'da `category = "içecekler"` ile `products[].category = "İçecekler"` yan yana
dönüyordu.

## 4. Para birimi — `FormatPrice`

Fiyatlar `18,00 TL` biçiminde yazılır (`ProductListTool` ve `ProductInquiryTool`, tek
yardımcı üzerinden).

> 🐞 **Bulundu ve düzeltildi — `$` sabit kodlanmıştı.** Çıktı `$18.00` şeklindeydi; Türkçe
> katalog, Türkçe mesajlar, dolar işareti. Kod tabanında başka hiçbir yerde para birimi
> biçimlendirmesi yoktu, yani bu bir konvansiyon değil iki noktada tekrarlanmış bir
> varsayımdı. Bu metin doğrudan LLM'e, oradan kullanıcıya gidiyor.

İki tasarım kararı:

- **Kültür açıkça tr-TR** verilir, ortamın `CurrentCulture`'ına bırakılmaz. Sunucu kültürü
  ortama göre değişir (container'larda genelde invariant) ve aynı kod yolu bir yerde
  `18,00`, başka yerde `18.00` üretirdi. Sabitlemeyle binlik ayıracı da doğru:
  `1.234,50 TL`.
- **Sembol yerine "TL"** yazılır. Metin hem LLM'e hem de sesli kanalda TTS'e gidiyor; `₺`
  sembolünün sesli okunuşu güvenilir değil, "TL" her iki tarafta da tek anlama geliyor.

## 5. Testler

`CustomerSupportBot.Api.Tests/Tools/ProductListToolTests.cs` (14 test, gerçek Postgres
fixture'ı ile). Her düzeltme mutasyon testiyle doğrulandı — ilgili satır eski davranışa
döndürüldüğünde kırmızıya dönen test sayısı:

| Mutasyon | Kırılan test |
|---|---|
| `Emit` sonucunu yok say | 1 |
| Boş kategori filtresini kaldır | 2 |
| `CategoryExists` dalını atla | 1 |
| Kültür sabitlemesini kaldır | 1 |

## Bağlantılar

- [OrderToolsService.md](OrderToolsService.md) — Benzer tool servisi
- [../../CustomerSupportBot.Domain/Model/CategoryProducts.md](../../CustomerSupportBot.Domain/Model/CategoryProducts.md) — Kategori sorgu sonucu
- [../../CustomerSupportBot.Application/Realtime/RealtimeNativeService.md](../Realtime/RealtimeNativeService.md) — Ambient bağlamın kurulmadığı kanal
- [../../CustomerSupportBot.Adapters.Persistence/PostgresAdapters.md](../../CustomerSupportBot.Adapters.Persistence/PostgresAdapters.md) — Repo tarafı

# OrderLineRequest

- **Kaynak:** `CustomerSupportBot.Domain/Model/OrderLineRequest.cs`
- **Tür:** `public sealed record`
- **Namespace:** `CustomerSupportBot.Domain.Model`

## 1. Ne İşe Yarar

`order_placement_tool`'un LLM'e açılan **tek bir sipariş satırı** parametresini temsil eder —
LLM'in serbestçe ürettiği, henüz doğrulanmamış ham girdi.

## 2. Hangi Amaçla Kullanılır

Bir müşteri "2 tane Dell XPS 15 ve 1 tane kablosuz mouse istiyorum" dediğinde, `OrderAgent`
`order_placement_tool`'u her ürün için bir `OrderLineRequest` içeren bir liste ile çağırır.
`[property: Description]` nitelikleri `AIFunctionFactory`'nin ürettiği JSON şemasına düşer —
yani LLM'e "bu alan ne bekliyor" bilgisini doğrudan bu attribute'lar üzerinden verir.

## 3. Sorumlulukları

- ✅ LLM'in girdisini (ürün adı + adet) taşımak
- ✅ Tool-şema description'ları ile LLM'i doğru doldurmaya yönlendirmek
- ❌ Ürün adını katalogla doğrulamak — bu `OrderToolsService`'in işi
- ❌ Doğrulanmış/kanonik veri taşımak — bkz. `OrderLine` (Application katmanı)

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Kim üretir:** LLM (`order_placement_tool` çağrısı sırasında, `AIFunctionFactory` şeması üzerinden)
- **Kim tüketir:** `OrderToolsService.OrderPlacementTool` — her satırı katalogda arayıp `OrderLine`'a çevirir

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 💡 **`OrderLine`'dan kasıtlı olarak ayrı bir tip.** `OrderLineRequest.ProductName` kullanıcının
> yazdığı serbest metindir, katalogda karşılığı olmayabilir. `OrderLine` ise katalogdan çözülmüş
> kanonik sonuçtur. İkisini tek bir tip yapmak, doğrulanmamış bir ürün adının domain'e sızmasına
> izin verirdi — "talep" (request) ile "sonuç" (result) ayrımı burada da uygulanmıştır (bkz.
> [OrderPlacementResult](OrderPlacementResult.md)'taki aynı prensip).

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `ProductName` | `string` | Sipariş verilecek ürünün adı (LLM'in yazdığı ham metin) |
| `Quantity` | `int` | Bu üründen kaç adet isteniyor (en az 1) |

## 7. Bağımlılıklar

Yok — saf domain modeli, dış bağımlılığı yok.

## Bağlantılar

- [OrderPlacementResult.md](OrderPlacementResult.md) — Bu satırların işlendiği nihai sonuç
- [StockDeductionResult.md](StockDeductionResult.md) — Stok düşümü sonucu

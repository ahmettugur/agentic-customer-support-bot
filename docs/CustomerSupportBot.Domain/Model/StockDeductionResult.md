# StockDeductionResult

- **Kaynak:** `CustomerSupportBot.Domain/Model/StockDeductionResult.cs`
- **Tür:** `public sealed record` (+ `public sealed record StockShortage`, aynı dosyada)
- **Namespace:** `CustomerSupportBot.Domain.Model`

## 1. Ne İşe Yarar

Çok satırlı bir siparişin (`OrderLineRequest[]`) stok düşümü işleminin sonucunu temsil eder —
işlem başarılı mı, değilse hangi ürünlerden ne kadar eksik.

## 2. Hangi Amaçla Kullanılır

Sipariş oluşturma sırasında her satır için stok düşülmeye çalışılır. `StockDeductionResult`
bu işlemin **hep-ya-da-hiç** sonucunu taşır: `Success=true` ise tüm satırlar düşülmüştür,
`Success=false` ise hiçbiri düşülmemiştir ve `Shortages` kullanıcıya "hangi üründen kaç adet
var" diyebilmek için yetersiz kalan satırları listeler.

## 3. Sorumlulukları

- ✅ Stok düşümünün başarı/başarısızlık durumunu taşımak
- ✅ Başarısızlıkta, yetersiz kalan her ürünü (istenen/mevcut adetle birlikte) raporlamak
- ❌ Veritabanı transaction'ını yönetmek — bu garanti adapter tarafında (repository implementasyonu) sağlanır
- ❌ Sipariş kaydını oluşturmak — bkz. [OrderPlacementResult](OrderPlacementResult.md)

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Kim üretir:** `IProductRepository`/`IOrderRepository` implementasyonu (Persistence katmanı) — stok
  düşümünü tek bir veritabanı transaction'ı içinde yapar
- **Kim tüketir:** `OrderToolsService.OrderPlacementTool` — başarısızsa `ToolResult.Conflict` ile
  `STOCK_INSUFFICIENT` hatası döner
- **Sarmalayan tip:** [OrderPlacementResult](OrderPlacementResult.md) — sipariş yazımıyla birlikte
  tek bir sonuç zarfına koyar

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 💡 **Ya hep ya hiç garantisi.** Satırlardan biri bile yetmezse hiçbiri düşülmez — aksi hâlde
> "3 üründen 2'si düşüldü, sipariş oluşmadı" gibi bir ara durumda stok sessizce kaybolurdu. Bu
> garanti adapter tarafında **tek bir veritabanı transaction'ı** ile sağlanır; domain modeli
> sadece nihai sonucu taşır, transaction mantığını bilmez.

## 6. Metotlar / Üyeler

### StockShortage

| Üye | Tip | Açıklama |
|---|---|---|
| `Product` | `string` | Kanonik ürün adı |
| `Requested` | `int` | İstenen adet |
| `Available` | `int` | Düşüm denenirken rafta bulunan adet |

### StockDeductionResult

| Üye | Tip | Açıklama |
|---|---|---|
| `Success` | `bool` | Tüm satırlar düşüldüyse `true` |
| `Shortages` | `IReadOnlyList<StockShortage>` | Başarısızlıkta yetersiz kalan satırlar; başarıda boş |
| `Ok()` (static) | `StockDeductionResult` | Başarılı sonuç factory'si (`Success=true`, boş liste) |
| `Insufficient(shortages)` (static) | `StockDeductionResult` | Başarısız sonuç factory'si |

## 7. Bağımlılıklar

Yok — saf domain modeli, dış bağımlılığı yok.

## Bağlantılar

- [OrderPlacementResult.md](OrderPlacementResult.md) — Bu sonucu saran nihai sipariş sonucu
- [OrderLineRequest.md](OrderLineRequest.md) — Düşümü tetikleyen talep satırı

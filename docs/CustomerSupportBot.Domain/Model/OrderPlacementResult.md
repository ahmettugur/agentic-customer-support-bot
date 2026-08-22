# OrderPlacementResult

- **Kaynak:** `CustomerSupportBot.Domain/Model/OrderPlacementResult.cs`
- **Tür:** `public sealed record`
- **Namespace:** `CustomerSupportBot.Domain.Model`

## 1. Ne İşe Yarar

Stok düşümü ve sipariş yazımının **tek transaction'daki ortak sonucunu** temsil eder — iki ayrı
adım (stok düş + sipariş yaz) tek bir atomik sonuç zarfına sarılır.

## 2. Hangi Amaçla Kullanılır

`OrderToolsService.OrderPlacementTool` sipariş oluşturma isteğini işlerken repository'den bir
`OrderPlacementResult` alır: `OrderId` doluysa hem stok düşülmüş hem sipariş kaydı yazılmıştır;
`Stock.Success=false` ise hiçbiri gerçekleşmemiştir. Bu sonuca göre tool ya `ToolResult.Ok`
(sipariş oluştu) ya da `ToolResult.Conflict` (stok yetersiz, `Stock.Shortages` kullanıcıya
gösterilir) döner.

## 3. Sorumlulukları

- ✅ "Sipariş oluştu mu" ve "stok durumu ne" bilgisini tek bir tutarlı sonuçta taşımak
- ✅ Ara durumların (stok düştü ama sipariş yok gibi) temsil EDİLEMEMESİNİ garanti etmek
- ❌ Transaction'ı yürütmek — bu Persistence katmanının işi

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Kim üretir:** `IOrderRepository` implementasyonu (Persistence katmanı) — stok düşümü + sipariş
  yazımını tek transaction'da yapıp sonucu bu tipe sarar
- **Kim tüketir:** `OrderToolsService.OrderPlacementTool`
- **İçerdiği tip:** [StockDeductionResult](StockDeductionResult.md)

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 💡 **"Stok düşüldü ama sipariş yok" diye bir ara durum temsil edilemez — kasıt budur.**
> `OrderId`'nin `null` olup olmaması ile `Stock.Success` bilgisi birbirine bağlı tutulur: tip
> sistemi, tutarsız bir kombinasyonun (`OrderId != null` ama `Stock.Success == false`) doğal
> akışta üretilmesini factory metotları (`Placed`/`OutOfStock`) dışında imkansız kılar.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `OrderId` | `string?` | Oluşan sipariş kimliği; başarısızlıkta `null` |
| `Stock` | `StockDeductionResult` | Stok düşümü sonucu (bkz. [StockDeductionResult](StockDeductionResult.md)) |
| `Placed(orderId)` (static) | `OrderPlacementResult` | Başarılı sonuç factory'si — `Stock = StockDeductionResult.Ok()` |
| `OutOfStock(stock)` (static) | `OrderPlacementResult` | Başarısız sonuç factory'si — `OrderId = null` |

## 7. Bağımlılıklar

Yok — saf domain modeli, dış bağımlılığı yok.

## Bağlantılar

- [StockDeductionResult.md](StockDeductionResult.md) — İçerdiği stok sonucu
- [OrderLineRequest.md](OrderLineRequest.md) — Bu sonucu doğuran talep satırları
- [OrderInfo.md](OrderInfo.md) — Oluşan siparişin tam modeli

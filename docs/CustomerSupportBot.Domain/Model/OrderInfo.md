# OrderInfo

**Dosya:** `Model/OrderInfo.cs`  
**Tür:** `class` (mutable)

## 1. Ne İşe Yarar

Bir siparişin domain modelidir — ürün adı, miktar, müşteri, durum ve iptal/iade bilgilerini taşır.

## 2. Hangi Amaçla Kullanılır

`IOrderRepository` sorguları ve `OrderToolsService` tool çağrıları bu modeli döner. `ToolResult.Data` alanında specialist agent'lara yapılandırılmış veri olarak sunulur.

## 3. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|-----|-----|----------|
| `Product` | `string` | Ürün adı |
| `Quantity` | `int` | Adet |
| `CustomerId` | `string` | Sipariş sahibi müşteri ID |
| `Status` | `string` | Durum (ör. "Kargolandı", "Teslim Edildi") |
| `OrderDate` | `DateTime` | Sipariş tarihi |
| `CancelledAt` | `DateTime?` | İptal tarihi |
| `CancelReason` | `string?` | İptal gerekçesi |
| `ReturnRequestedAt` | `DateTime?` | İade talep tarihi |
| `ReturnReason` | `string?` | İade gerekçesi |

## Bağlantılar

- [ToolResult.md](ToolResult.md) — Bu model `Data` alanında taşınır
- [ComplaintInfo.md](ComplaintInfo.md) — Şikayet modeli
- [ProductInfo.md](ProductInfo.md) — Ürün modeli

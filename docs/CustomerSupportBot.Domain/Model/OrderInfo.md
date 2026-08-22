# OrderInfo & OrderLine

**Dosya:** `Model/OrderInfo.cs`
**Tür:** `OrderInfo` → `class` (mutable), `OrderLine` → `sealed record`

## 1. Ne İşe Yarar

Bir siparişin domain modelidir. Bir sipariş **birden fazla ürün satırı** taşıyabilir; her satır bir `OrderLine` (ürün adı + adet) olarak `Lines` listesinde durur. Sipariş başlığı (müşteri, durum, tarih, iptal/iade bilgisi) `OrderInfo` üzerindedir.

```
OrderInfo (başlık)
├── CustomerId, Status, OrderDate, CancelledAt, ReturnRequestedAt...
└── Lines: [ OrderLine("Kahve", 2), OrderLine("Çikolata", 1) ]
```

## 2. Hangi Amaçla Kullanılır

`IOrderRepository` sorguları ve `OrderToolsService` tool çağrıları bu modeli döner. `ToolResult.Data` alanında specialist agent'lara yapılandırılmış veri olarak sunulur.

> 💡 **Analiz notu:** Bir alışveriş fişi gibi düşünün — üstte kim/ne zaman/hangi durumda bilgisi, altta satır satır ürünler.

## 3. Neden çok satırlı? (tarihçe — bilmeniz gereken tuzak)

Veritabanı şeması **en baştan beri** çok satırlıydı: `catalog.order_details` tablosunda sipariş başına N kayıt tutulur, birincil anahtarı `(order_code, product_id)`'dir. Ancak domain modeli uzun süre tek bir `Product` + `Quantity` alanı taşıdı ve `OrderRepository.MapToModel` içinde `Details.FirstOrDefault()` çağrılıyordu.

Sonuç: ikinci ve sonraki ürünler **sessizce kayboluyordu** ve çok ürünlü sipariş vermek pratikte imkânsızdı. `Lines` bu boşluğu kapatır — artık tek gerçek kaynak bu listedir.

**Bu yüzden `Product` ve `Quantity` alanları kaldırıldı.** "İlk satırı dönen" bir uyumluluk özelliği bırakmak, tam da düzeltilen hatanın sessizce geri gelmesine davetiye olurdu.

## 4. Metotlar / Üyeler

### `OrderLine`

| Üye | Tip | Açıklama |
|-----|-----|----------|
| `Product` | `string` | Ürünün **kanonik** adı — katalogdan çözülmüş hâli, kullanıcının yazdığı ham metin değil |
| `Quantity` | `int` | Adet; her zaman ≥ 1 |

### `OrderInfo`

| Üye | Tip | Açıklama |
|-----|-----|----------|
| `Lines` | `List<OrderLine>` | Sipariş satırları. Geçerli siparişte en az bir eleman; aynı ürün iki kez yer almaz |
| `CustomerId` | `string` | Sipariş sahibi müşteri ID |
| `Status` | `string` | Durum (ör. "Kargolandı", "Teslim Edildi") |
| `OrderDate` | `DateTime` | Sipariş tarihi |
| `CancelledAt` | `DateTime?` | İptal tarihi |
| `CancelReason` | `string?` | İptal gerekçesi |
| `ReturnRequestedAt` | `DateTime?` | İade talep tarihi |
| `ReturnReason` | `string?` | İade gerekçesi |
| `LinesSummary()` | `string` | İnsan-okunur özet: `"Kahve x2, Çikolata x1"`. Boş listede `"—"` |
| `TotalQuantity()` | `int` | Satırların adet toplamı |

### Neden `LinesSummary()` var?

Sistemde string bekleyen birkaç yer var — tool mesajları, müşteri bağlamı prompt'u, `VerifiedEntity.Attributes` (düz `string→string` sözlük). Bu noktalarda satır yapısı taşınamaz. `LinesSummary()` tek yerde tanımlı bir biçim sunar; her çağrı yerinin kendi `string.Join`'ini yazması hem tekrar hem tutarsızlık olurdu.

Makine tarafından okunması gereken taraf (ör. `ResponseAgent`) bu özeti değil, `ToolResult.Data`'daki yapılandırılmış `lines` dizisini kullanır.

## 5. İlgili tipler

| Tip | Dosya | Ne için |
|-----|-------|---------|
| `OrderLineRequest` | `Model/OrderLineRequest.cs` | LLM'in ürettiği **doğrulanmamış** satır talebi (ürün adı serbest metin). `OrderLine` ise katalogdan çözülmüş sonuçtur. İkisi bilinçli olarak ayrı tiplerdir — doğrulanmamış bir ürün adının domain'e sızmasını engeller |
| `StockDeductionResult` / `StockShortage` | `Model/StockDeductionResult.cs` | Çok satırlı stok düşümünün "ya hep ya hiç" sonucu ve yetersiz kalan satırlar |

## Bağlantılar

- [ToolResult.md](ToolResult.md) — Bu model `Data` alanında taşınır
- [../../CustomerSupportBot.Application/Services/Tools/OrderToolsService.md](../../CustomerSupportBot.Application/Services/Tools/OrderToolsService.md) — Siparişi oluşturan tool
- [../../CustomerSupportBot.Adapters.Persistence/Postgres/Repositories.md](../../CustomerSupportBot.Adapters.Persistence/Postgres/Repositories.md) — `OrderRepository` ve `order_details` eşlemesi
- [ComplaintInfo.md](ComplaintInfo.md) — Şikayet modeli
- [ProductInfo.md](ProductInfo.md) — Ürün modeli

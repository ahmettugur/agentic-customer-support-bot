# OrderToolsService

- **Kaynak:** `Services/Tools/OrderToolsService.cs`
- **Tür:** `public sealed class : IOrderToolsService`
- **Namespace:** `CustomerSupportBot.Application.Services.Tools`

## 1. Ne İşe Yarar

Sipariş yaşam döngüsüyle ilgili tüm ajan-çağrılabilir tool'ları barındırır: sipariş oluşturma,
durum sorgulama, son/tüm siparişleri listeleme, iptal ve iade talebi. Her metot bir MAF
tool'u olarak `[Description]` attribute'larıyla işaretlenmiştir ve `ToolResult` döner.

## 2. Hangi Amaçla Kullanılır

`OrderAgent` (Adapters.Agents) ve `ApprovalGateService`'in bu servise doğrudan bağımlı olarak
sipariş işlemlerini gerçekleştirmesi için — LLM'in doğal dil isteğini somut, doğrulanmış bir
veritabanı işlemine çevirmek.

## 3. Sorumlulukları

**Üstlendiği:**
- Girdi doğrulama (boş/geçersiz alanlar, adet ≤ 0, min. uzunluk kuralları).
- Sipariş sahipliği kontrolü (`ValidateOrderActionable`) — `customerId` uyuşmazlığında
  **"bulunamadı" ile aynı** hata döndürmek (bkz. madde 5).
- Mükerrer sipariş koruması (`SideEffectIdempotencyCache` ile).
- Katalog çözümlemesi ve aynı üründen birden fazla satırın birleştirilmesi.

**Üstlenmediği:** HITL onay akışı (`ApprovalGateService`'in işi — bu servis onay alındıktan
**sonra** çağrılır); `customerId`'nin JWT'den mi yoksa parametreden mi geldiği (çağıranın
sorumluluğu — bkz. `ApprovalGateService` dokümanı, `IApprovalContextAccessor.Context.CustomerId`
enjeksiyonu).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IOrderRepository`, `IProductCatalogRepository`, `ICustomerRepository` — kalıcılık portları.
- [`SideEffectIdempotencyCache`](SideEffectIdempotencyCache.md) — mükerrer çağrı koruması.
- `ApprovalGateService` (Adapters.Agents) — `OrderPlacementTool`/`OrderCancelTool`/
  `ReturnRequestTool` HITL onayı gerektiren tool'lar olarak buradan sarılır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

### `OrderNotAccessibleMessage` — enumeration oracle'a karşı bilinçli tekilleştirme

Sipariş "yok" ile "var ama başkasının" durumları **aynı** kullanıcı mesajıyla bildirilir:
`"'{orderId}' numaralı sipariş bulunamadı."`. Neden: sipariş numaraları 4 haneli ve ardışık
olduğu için (seed: 1030–1081), bu iki durum farklı metinlerle bildirilseydi bir saldırgan
tarama yapıp "bulunamadı" = yok, "size ait değil" = **var ama başkasının** çıkarımını
yapabilirdi (klasik enumeration oracle açığı). Hata **kodu**
(`WellKnown.ToolErrorCodes.CustomerIdMismatch` vs `OrderNotFound`) operasyonel görünürlük için
korunur — trace/admin panelinde gerçek sebep görünür, ama kullanıcıya giden metin aynıdır.

### Doğrulama sıralaması neden bu şekilde (`OrderPlacementTool`)

Ucuz ve yan etkisiz kontroller (biçim, müşteri varlığı, katalog çözümü) önce çalışır; stok
yalnızca her şey geçerliyse düşülür. Böylece geçersiz bir talep stoğa hiç dokunmaz.

### Katalog hataları neden tek seferde toplu bildirilir

Tüm satırlar denenir, bulunamayanlar tek seferde bildirilir — ilk hatada durulsaydı kullanıcı,
çok ürünlü bir siparişteki eksikleri tur tur öğrenirdi (ping-pong etkisi).

### Aynı ürünün birden fazla satırda birleştirilmesi zorunlu

`order_details` tablosunun birincil anahtarı `(order_code, product_id)` — tekrar eden ürün
ikinci `INSERT`'te bütünlük ihlali verirdi. Birleştirme sonrası satırlar `Product` adına göre
sıralanır ki idempotency imzası satırların **geliş sırasından bağımsız** olsun.

### Stok düşümü + sipariş yazımı neden tek transaction'da

Ayrı yapıldıklarında aradaki bir hata (DB kesintisi, retry tükenmesi, pod'un ölmesi) stoğu
düşülmüş ama karşılığında hiçbir sipariş oluşmamış hâlde bırakıyordu — hiçbir yerde hata
görünmeden, ürün stoğu kalıcı olarak azalarak. `IOrderRepository.PlaceOrder` bu ikisini
atomik yapar.

### Mükerrer çağrı koruması neden stok düşümünden önce

İmza (`customerId` + kanonik satır özeti) kontrol edilip eşleşme bulunursa, stok hiç
düşülmeden önceki sipariş numarası döndürülür — "kahve"/"Kahve" gibi büyük/küçük harf
farkları imzayı bozmasın diye kanonik ürün adları kullanılır.

### Sahiplik kontrolü neden `OrderCancelTool`/`ReturnRequestTool`'da tekrar yapılır

`ApprovalGateService` bunu onay kaydı oluşturulmadan **önce** de çalıştırır; burada tekrar
edilir çünkü durum iki an (onay talebi oluşturma ↔ admin kararı) arasında değişmiş olabilir —
TOCTOU (time-of-check to time-of-use) riskine karşı savunma.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `OrderPlacementTool(lines, customerId)` | Yeni sipariş oluşturur. Sıra: girdi doğrulama → müşteri varlığı → katalog çözümü (toplu hata) → satır birleştirme → idempotency kontrolü → stok+yazım (tek transaction) → sonuç. Stok yetersizse `ToolResult.Conflict` (hangi üründe ne kadar eksik olduğu detaylı listelenir). |
| `ValidateOrderActionable(orderId, customerId)` | Ortak sahiplik/varlık kontrolü — `orderId` boşsa, sipariş yoksa veya başka müşteriye aitse (aynı mesajla, bkz. madde 5) bir `ToolResult` döner; her şey uygunsa `null` (yani "devam et"). |
| `OrderStatusTool(orderId, customerId)` | `ValidateOrderActionable` + durumu döner. |
| `GetLastOrderTool(customerId)` | Müşterinin en son siparişini döner; hiç yoksa `NoOrdersForCustomer`. |
| `GetAllOrdersTool(customerId)` | Müşterinin **tüm** siparişlerini (kapasitesiz) listeler. |
| `OrderCancelTool(orderId, reason, customerId)` | `reason` en az 5 karakter (trim edilmiş) olmalı; sahiplik kontrolü; zaten iptalse `OrderAlreadyCancelled`; yalnızca "İşleniyor"/"Kargolandı" durumundaki siparişler iptal edilebilir. |
| `ReturnRequestTool(orderId, reason, customerId)` | Aynı doğrulama deseni; yalnızca "Teslim Edildi" ve 14 gün içindeki siparişler iade edilebilir; zaten iade talebi varsa `ReturnAlreadyRequested`. |
| `FormatLines(lines)` *(private static)* | `"Kahve x2, Çay x1"` biçiminde insan-okunur özet. |
| `ToLineData(lines)` *(private static)* | `ToolResult.Data` içinde taşınan makine-okunur satır listesi. |
| `OrderNotAccessibleMessage(orderId)` *(private static)* | Enumeration oracle'a karşı tekilleştirilmiş "bulunamadı" mesajı — bkz. madde 5. |

## 7. Bağımlılıklar

Constructor injection ile: `IOrderRepository`, `IProductCatalogRepository`,
`ICustomerRepository`, `SideEffectIdempotencyCache?` (opsiyonel — verilmezse `new
SideEffectIdempotencyCache()` ile kendi örneği oluşturulur, testlerde kolayca izole edilebilsin
diye).

## Bağlantılar

- [SideEffectIdempotencyCache.md](SideEffectIdempotencyCache.md) — mükerrer koruma mekanizması
- [../../../CustomerSupportBot.Adapters.Agents/ApprovalGateService.md](../../../CustomerSupportBot.Adapters.Agents/ApprovalGateService.md) — HITL sarmalayıcı

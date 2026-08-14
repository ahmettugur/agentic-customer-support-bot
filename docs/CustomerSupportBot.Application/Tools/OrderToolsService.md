# OrderToolsService

**Dosya:** `Services/Tools/OrderToolsService.cs`
**Port:** `IOrderToolsService` (Application/Ports/Outbound)

## 1. Ne İşe Yarar

Sipariş domain tool'larını implement eder — sipariş **oluşturma**, sorgulama, listeleme, **iptal**, **iade talebi**.

## 2. Hangi Amaçla Kullanılır

`OrderAgent` bu tool'ları çağırır. Yan etkili üçü (oluşturma, iptal, iade) doğrudan çağrılmaz; `ApprovalGateService` tarafından sarılıp HITL onay kuyruğuna düşer, admin onayladıktan sonra `ApprovalExecutionRouter` üzerinden buraya geri döner.

> 💡 **Analiz notu:** Sipariş departmanı gibi — "sipariş ver", "siparişi iptal et", "iade iste" isteklerini gerçekleştirir.

## 3. Tool'lar

| Metot | Yan etkili? | Parametreler |
|-------|-------------|--------------|
| `OrderPlacementTool` | ✓ (HITL) | `IReadOnlyList<OrderLineRequest> lines`, `customerId` |
| `OrderStatusTool` | — | `orderId`, `customerId` |
| `GetLastOrderTool` | — | `customerId` |
| `GetAllOrdersTool` | — | `customerId` |
| `OrderCancelTool` | ✓ (HITL) | `orderId`, `reason`, `customerId` |
| `ReturnRequestTool` | ✓ (HITL) | `orderId`, `reason`, `customerId` |
| `ValidateOrderActionable` | — | `orderId`, `customerId` — salt-okunur ön kontrol |

> 🔒 Tüm metotlardaki `customerId` **LLM parametresi değildir**. Çağıran taraf (`ApprovalGateService`) bunu her zaman login'li kullanıcının JWT'den gelen doğrulanmış kimliğinden geçirir.

## 4. `OrderPlacementTool` — çok ürünlü sipariş

Tek çağrıda birden fazla ürün satırı alır: *"2 kahve ve 1 çikolata"* → tek `lines` dizisi → **tek sipariş**.

### Neden tek çağrı, ürün başına ayrı çağrı değil?

Her tool çağrısı ayrı bir onay kaydı üretirdi. Admin bunları tek tek görür, birini onaylayıp diğerini reddedebilirdi — müşteri yarım bir sipariş alırdı. **Tek çağrı = tek onay = tek sipariş.**

### Adım adım ne yapar

Sıra kasıtlıdır: ucuz ve yan etkisiz kontroller önce, stok en sonda. Böylece geçersiz bir talep stoğa hiç dokunmaz.

| # | Adım | Hata durumunda |
|---|------|----------------|
| 1 | `customerId` boş mu | `ValidationError` (`customer_id`) |
| 2 | `lines` boş mu | `ValidationError` (`lines`) |
| 3 | Her satırda ürün adı var mı | `ValidationError` (`product_name`) |
| 4 | Adetler ≥ 1 mi | `ValidationError` (`quantity`) — **hatalı satırların ürün adları mesajda listelenir** |
| 5 | Müşteri kayıtlı mı | `NotFound` (`CUSTOMER_NOT_FOUND`) |
| 6 | Katalog çözümlemesi — **tüm** satırlar denenir | `NotFound` (`PRODUCT_NOT_FOUND`), bulunamayanların **hepsi tek mesajda** |
| 7 | Aynı ürünün satırları birleştirilir (adetler toplanır) | — |
| 8 | Mükerrer çağrı kontrolü (`SideEffectIdempotencyCache`) | Yeni kayıt yazılmaz, önceki sipariş numarası bildirilir |
| 9 | Stok düşümü — **atomik** | `Conflict` (`STOCK_INSUFFICIENT`), hangi üründen kaç adet kaldığı |
| 10 | Sipariş yazılır | — |

### Neden 4. ve 6. adımda hatalar toplu bildiriliyor?

İlk hatada durulsaydı, kullanıcı 5 ürünlük bir siparişteki eksikleri **tur tur** öğrenirdi (ping-pong). Sistemin genel kuralı: eksik bilgi tek mesajda istenir.

### Neden 7. adım (birleştirme) zorunlu?

`order_details` tablosunun birincil anahtarı `(order_code, product_id)`. Aynı ürün iki satırda gelirse ikinci `INSERT` anahtar ihlali verirdi. LLM'den bunu doğru yapması beklenmez — sistem toplar. Birleştirme aynı zamanda satırları ada göre sıralar, böylece **idempotency imzası satır sırasından bağımsız** olur (`[Kahve, Çay]` ile `[Çay, Kahve]` aynı sipariştir).

### Neden 9. adım atomik olmak zorunda?

Satırlar tek tek düşülseydi, üçüncü satır yetmediğinde ilk ikisinin stoğu düşmüş ama sipariş oluşmamış olurdu — stok sessizce kaybolurdu. `IProductCatalogRepository.TryDeductStock(lines)` tek transaction'da çalışır: bir satır bile yetmezse hiçbiri düşülmez.

Kullanıcıya dönen mesaj bunu açıkça söyler: *"Siparişin tamamı iptal edildi — hiçbir ürün rezerve edilmedi."* `OrderAgent` prompt'unda da "diğer ürünler alındı" demesi yasaklanmıştır.

### Dönen `ToolResult.Data`

```json
{
  "orderId": "1082",
  "lines": [ { "product": "Kahve", "quantity": 2 }, { "product": "Çikolata", "quantity": 1 } ],
  "totalQuantity": 3,
  "customerId": "1027",
  "status": "İşleniyor"
}
```

Sorgulama tool'ları (`OrderStatusTool`, `GetLastOrderTool`, `GetAllOrdersTool`, `ReturnRequestTool`) de aynı `lines` + `totalQuantity` biçimini kullanır; mesaj metinlerinde ise `OrderInfo.LinesSummary()` geçer (`"Kahve x2, Çikolata x1"`).

## 5. Güvenlik notu — sipariş numarası sızıntısı

`ValidateOrderActionable`, "sipariş yok" ile "sipariş var ama başkasının" durumlarını **aynı metinle** reddeder (`OrderNotAccessibleMessage`). Sipariş numaraları ardışık olduğu için farklı metinler bir enumeration oracle'ı yaratırdı. Hata **kodu** (`CUSTOMER_ID_MISMATCH`) operasyonel görünürlük için korunur — trace ve admin panelinde gerçek sebep görünür.

## Bağlantılar

- [../../CustomerSupportBot.Domain/Model/OrderInfo.md](../../CustomerSupportBot.Domain/Model/OrderInfo.md) — `OrderInfo` / `OrderLine` / `OrderLineRequest`
- [../Approval/ApprovalExecutionRouter.md](../Approval/ApprovalExecutionRouter.md) — Onaydan sonra bu tool'u çağıran yönlendirici
- [../../CustomerSupportBot.Adapters.Agents/Team/OrderAgent.md](../../CustomerSupportBot.Adapters.Agents/Team/OrderAgent.md) — Tool'u çağıran ajan
- [ComplaintToolsService.md](ComplaintToolsService.md) — Benzer tool servisi
- [ProductToolsService.md](ProductToolsService.md) — Benzer tool servisi

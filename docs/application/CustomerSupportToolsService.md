# CustomerSupportToolsService

**Dosya:** `CustomerSupportBot.Application/Services/Tools/CustomerSupportToolsService.cs`  
**Implements:** `ICustomerSupportToolsService`  
**Yaşam döngüsü:** Singleton

## Ne yapar?

`ICustomerSupportToolsService` için **facade** implementasyonu. Tüm tool çağrılarını üç alt servise delege eder:

- `ProductToolsService` → `IProductToolsService`
- `OrderToolsService` → `IOrderToolsService`
- `ComplaintToolsService` → `IComplaintToolsService`

Her sub-servis kendi repository bağımlılıklarını taşır; `CustomerSupportToolsService` hiçbir repository'ye doğrudan erişmez — sadece iletir.

Her tool bir `ToolResult` döndürür — yapılandırılmış sonuç formatı.

## Tool listesi

| Metot | Tool adı | Yan etkisi var mı? | HITL gerekir mi? |
|-------|---------|-------------------|-----------------|
| `ProductInquiryTool` | `product_inquiry_tool` | Hayır | Hayır |
| `ProductListTool` | `product_list_tool` | Hayır | Hayır |
| `OrderStatusTool` | `order_status_tool` | Hayır | Hayır |
| `GetLastOrderTool` | `get_last_order_tool` | Hayır | Hayır |
| `GetAllOrdersTool` | `get_all_orders_tool` | Hayır | Hayır |
| `OrderPlacementTool` | `order_placement_tool` | **Evet** | **Evet** |
| `OrderCancelTool` | `order_cancel_tool` | **Evet** | **Evet** |
| `ReturnRequestTool` | `return_request_tool` | **Evet** | **Evet** |
| `ComplaintRegistrationTool` | `complaint_registration_tool` | **Evet** | **Evet** |
| `HumanHandoffTool` (static) | `human_handoff_tool` | Hayır | Hayır |

> `OrderPlacementTool`, `OrderCancelTool`, `ReturnRequestTool` ve `ComplaintRegistrationTool` doğrudan çağrılmaz — `ApprovalGateService` bunları HITL kapısına sarar ve `AIFunction` olarak ajana verir. Bu sınıftaki metotlar yalnızca onay geldikten sonra çağrılır.

## `ToolResult` yapısı

Her tool bir `ToolResult` döndürür. Bu nesne ajanın nasıl davranacağını belirler:

```csharp
ToolResult.Ok(message, data, confidence)    // Başarılı
ToolResult.NotFound(errorCode, message)     // Kayıt bulunamadı
ToolResult.ValidationError(message, fields) // Parametre eksik/hatalı
ToolResult.Conflict(errorCode, message)     // İş kuralı ihlali (stok yok, müşteri uyuşmuyor)
```

## Tool detayları

### `ProductInquiryTool`

Ürün adına göre katalogda arama yapar (`IProductCatalogRepository.FindProduct`).

- Tam eşleşme → `confidence = 1.0`
- Kısmi eşleşme → `confidence = 0.85`
- Ürün yoksa → `ToolResult.NotFound`

### `ProductListTool`

Tüm katalogu veya belirli bir kategoriye ait ürünleri listeler (`IProductCatalogRepository.GetAll` / `GetByCategory`).

- `category` opsiyoneldir; boş gelirse tüm ürünler döner
- Kategori karşılaştırması `LOWER()` ile yapılır — büyük/küçük harf duyarsız
- Hiç ürün yoksa → `ToolResult.NotFound`

### `OrderStatusTool`

`orderId` ile tek sipariş getirir (`IOrderRepository.Get`).

- Sipariş var → durum, ürün, adet, tarih döner
- Sipariş yoksa → `NotFound`

### `GetLastOrderTool`

Müşterinin en son siparişini getirir (`IOrderRepository.GetLast`).

- Sipariş var → `ToolResult.Ok`
- Hiç sipariş yoksa → `NotFound`

### `GetAllOrdersTool`

Müşterinin tüm siparişlerini listeler. En fazla 5'i metin olarak formatlar; `data.orders` içinde tamamı döner.

### `OrderPlacementTool`

**Yan etkilidir** — stok düşürür, sipariş oluşturur.

Adımlar:
1. Parametre doğrulama (`customerId`, `productName`, `quantity`)
2. Müşteri var mı? (`ICustomerRepository`)
3. Ürün katalogda var mı?
4. **Mükerrer çağrı kontrolü** (`SideEffectIdempotencyCache`) — stok düşülmeden önce
5. Stok yeterli mi? (`IProductCatalogRepository.TryDeductStock`)
6. Sipariş oluştur (`IOrderRepository.Create`) ve sonucu cache'e kaydet

**Stok yetersizse:** `ToolResult.Conflict` → ajan müşteriye bildirir, yeni sipariş oluşturmaz.

**Mükerrer çağrıda** (aynı ürün + adet + müşteri, 60 saniye içinde): stok düşülmez, sipariş yazılmaz. `ToolResult.Ok` döner ama mesaj ilk siparişin numarasını taşır ve `Data.duplicate = true` olur:

```
"Bu siparişi az önce oluşturmuştum — sipariş numarası: 1082. Mükerrer kayıt
 oluşturmadım. Gerçekten ikinci bir sipariş istiyorsanız lütfen açıkça belirtin."
```

Sonuç sessizce taklit edilmediği için meşru bir tekrar sipariş talebi kaybolmaz — ajan kullanıcıya durumu bildirip teyit isteyebilir. Detay: [security.md §4.2](../security.md#42-tool-idempotency).

### `OrderCancelTool`

**Yan etkilidir** — sipariş durumunu `İptal Edildi` olarak değiştirir.

Adımlar:
1. Parametre doğrulama (`orderId` zorunlu, `reason` trim sonrası min 5 karakter)
2. Sipariş var mı?
3. Zaten iptal edilmiş mi? → `ToolResult.Conflict(OrderAlreadyCancelled)`
4. `IOrderRepository.Cancel(orderId, reason)` çağrısı
5. İptal edilemez durumdaysa (`Delivered` vb.) → `ToolResult.Conflict(OrderNotCancellable)`

**İptal edilebilir durumlar:** `İşleniyor`, `Kargolandı`  
**İptal edilemez durumlar:** `Teslim Edildi`, `İptal Edildi`, `İade Talep Edildi`

### `ReturnRequestTool`

**Yan etkilidir** — sipariş durumunu `İade Talep Edildi` olarak değiştirir.

Adımlar:
1. Parametre doğrulama (`orderId` zorunlu, `reason` trim sonrası min 5 karakter)
2. Sipariş var mı?
3. Zaten iade talebi var mı? → `ToolResult.Conflict(ReturnAlreadyRequested)`
4. `IOrderRepository.RequestReturn(orderId, reason)` çağrısı
5. Uygun değilse → `ToolResult.Conflict(ReturnNotEligible)` (durum/süre)

**İade edilebilir:** `Teslim Edildi` durumunda, **14 gün** içinde  
**İade edilemez:** Diğer tüm durumlar veya süre aşımı  
**Ücret iadesi:** Onay sonrası 5–7 iş günü aynı ödeme yöntemiyle

### `ComplaintRegistrationTool`

**Yan etkilidir** — şikayet oluşturur.

Adımlar:
1. Parametre doğrulama (`orderId` zorunlu, `complaintText` trim sonrası en az 10 karakter)
2. Sipariş var mı?
3. `customerId` verilmemişse siparişten otomatik türetilir (`inferred=true`)
4. `customerId` verilmişse sipariş sahibiyle eşleşiyor mu?
5. **Mükerrer çağrı kontrolü** (`SideEffectIdempotencyCache`) — kayıt oluşturulmadan önce
6. Şikayet oluştur (`IComplaintRepository.Create`) ve sonucu cache'e kaydet

**CustomerID uyuşmazlığı:** `ToolResult.Conflict(CustomerIdMismatch)` — başkasının siparişine şikayet açılmasını engeller.

**Mükerrer çağrıda** (aynı `orderId` + türetilmiş `customerId` + şikayet metni, 60 saniye içinde): yeni kayıt oluşturulmaz, ilk şikayet numarasını bildiren bir `ToolResult.Ok` döner (`Data.duplicate = true`). İmza **türetilmiş** `customerId` üzerinden kurulduğu için, `customerId`'nin bir çağrıda verilip diğerinde verilmemesi aynı şikayeti iki farklı çağrı gibi göstermez.

### `HumanHandoffTool` (static)

**Yan etkisi yoktur.** Yalnızca kullanıcının handoff talebini formalleştirir. Gerçek yönlendirme `EscalationPolicyService` ve `HumanHandoffAgent` tarafından yapılır.

## Sub-servis bağımlılıkları

| Sub-servis | Bağımlılıklar |
|-----------|---------------|
| `ProductToolsService` | `IProductCatalogRepository`, `IUiHintEmitter` |
| `OrderToolsService` | `IOrderRepository`, `IProductCatalogRepository`, `ICustomerRepository` |
| `ComplaintToolsService` | `IComplaintRepository`, `IOrderRepository` |

## Yeni tool eklemek

1. Uygun sub-servis arayüzüne (`IProductToolsService`, `IOrderToolsService`, `IComplaintToolsService`) metot ekleyin.
2. İlgili sub-serviste implement edin. `[Description("...")]` attribute'u LLM'nin tool'u ne zaman çağıracağını belirler.
3. `ICustomerSupportToolsService` arayüzüne metot ekleyin ve `CustomerSupportToolsService` facade'ında sub-servise delege edin.
4. HITL gerektiriyorsa `ApprovalGateService`'de yeni bir `Build___Tool()` metodu yazın ve `appsettings.json`'da `ToolsRequiringApproval` listesine ekleyin.
5. `CustomerSupportTeam` constructor'ında ilgili ajana tool olarak atayın.

## Parametre isim sabitleri

`WellKnown.ToolParameterNames` içindeki sabitleri kullanın — string literal yazmayın:

```csharp
WellKnown.ToolParameterNames.OrderId         // "order_id"
WellKnown.ToolParameterNames.CustomerId      // "customer_id"
WellKnown.ToolParameterNames.ProductName     // "product_name"
WellKnown.ToolParameterNames.Quantity        // "quantity"
WellKnown.ToolParameterNames.Reason          // "reason"
```

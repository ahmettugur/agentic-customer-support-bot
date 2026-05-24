# CustomerSupportToolsService

**Dosya:** `CustomerSupportBot.Application/Services/CustomerSupportToolsService.cs`  
**Implements:** `ICustomerSupportToolsService`  
**Yaşam döngüsü:** Singleton

## Ne yapar?

Ajanların LLM üzerinden çağırabileceği 6 tool'un gerçek iş mantığını uygular. Her tool bir `ToolResult` döndürür — yapılandırılmış sonuç formatı. Repository port'ları üzerinden veri erişimi yapar; veritabanı implementasyonuna bağımlı değildir.

## Tool listesi

| Metot | Tool adı | Yan etkisi var mı? | HITL gerekir mi? |
|-------|---------|-------------------|-----------------|
| `ProductInquiryTool` | `product_inquiry_tool` | Hayır | Hayır |
| `OrderStatusTool` | `order_status_tool` | Hayır | Hayır |
| `GetLastOrderTool` | `get_last_order_tool` | Hayır | Hayır |
| `GetAllOrdersTool` | `get_all_orders_tool` | Hayır | Hayır |
| `OrderPlacementTool` | `order_placement_tool` | **Evet** | **Evet** |
| `ComplaintRegistrationTool` | `complaint_registration_tool` | **Evet** | **Evet** |
| `HumanHandoffTool` (static) | `human_handoff_tool` | Hayır | Hayır |

> `OrderPlacementTool` ve `ComplaintRegistrationTool` doğrudan çağrılmaz — `ApprovalGateService` bunları HITL kapısına sarar ve `AIFunction` olarak ajana verir. Bu sınıftaki metotlar yalnızca onay geldikten sonra çağrılır.

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
2. Ürün katalogda var mı?
3. İdempotency cache kontrolü (son 60 saniye aynı istek gelmiş mi?)
4. Stok yeterli mi? (`IProductCatalogRepository.TryDeductStock`)
5. Sipariş oluştur (`IOrderRepository.Create`)
6. Sonucu idempotency cache'e kaydet

**Stok yetersizse:** `ToolResult.Conflict` → ajan müşteriye bildirir, yeni sipariş oluşturmaz.

### `ComplaintRegistrationTool`

**Yan etkilidir** — şikayet oluşturur.

Adımlar:
1. Parametre doğrulama (`orderId` zorunlu, `complaintText` en az 10 karakter)
2. Sipariş var mı?
3. `customerId` verilmemişse siparişten otomatik türetilir (`inferred=true`)
4. `customerId` verilmişse sipariş sahibiyle eşleşiyor mu?
5. İdempotency cache kontrolü
6. Şikayet oluştur (`IComplaintRepository.Create`)

**CustomerID uyuşmazlığı:** `ToolResult.Conflict(CustomerIdMismatch)` — başkasının siparişine şikayet açılmasını engeller.

### `HumanHandoffTool` (static)

**Yan etkisi yoktur.** Yalnızca kullanıcının handoff talebini formalleştirir. Gerçek yönlendirme `EscalationPolicyService` ve `HumanHandoffAgent` tarafından yapılır.

## İdempotency mekanizması

Yan etkili tool'lar 60 saniyelik bir idempotency cache kullanır. Bu cache LLM'nin aynı parametrelerle tool'u tekrar çağırmasını önler.

```csharp
private readonly ConcurrentDictionary<string, (DateTime At, ToolResult Result)> _idempotencyCache;
private static readonly TimeSpan IdempotencyWindow = TimeSpan.FromSeconds(60);
```

**Anahtar nasıl hesaplanır?**

```csharp
private static string ComputeKey(string toolName, params string?[] parts)
{
    var raw = string.Join("|", parts.Select(p => p?.Trim().ToLowerInvariant() ?? ""));
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
    return $"{toolName}:{Convert.ToHexString(bytes)[..16]}"; // 16 hex karakter ≈ 64 bit
}
```

Parametreler küçük harfe çevrilip birleştirildikten sonra SHA256 hash'i alınır. "Laptop" ve "laptop" aynı key üretir.

**Cache temizliği:** Cache 200'den fazla giriş içerirse eski girişler (`> 60s`) otomatik temizlenir.

**Önemli:** Cache Singleton servis içinde bellekte tutulur. Pod restart veya yeni pod başlatıldığında sıfırlanır — çok kısa aralıklı pod restart'larında teorik duplicate işlem riski vardır.

## Yeni tool eklemek

1. `ICustomerSupportToolsService` arayüzüne metot ekleyin.
2. Bu sınıfta implement edin. `[Description("...")]` attribute'u LLM'nin tool'u ne zaman çağıracağını belirler — açıklayıcı yazın.
3. Yan etkisi varsa idempotency cache ekleyin.
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

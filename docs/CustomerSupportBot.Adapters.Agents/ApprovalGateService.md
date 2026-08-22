# ApprovalGateService

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/ApprovalGateService.cs`
- **Tür:** `public class`
- **Namespace:** `CustomerSupportBot.Adapters.Agents`

## Ne işe yarar?

`ApprovalGateService`, Human-in-the-Loop (HITL) onay kapısı ve eskalasyon yönetim servisidir. Yan etkili 4 araç çağrısını (sipariş oluşturma, sipariş iptali, iade talebi, şikayet kaydı) yakalar; bloklamayan (non-blocking) asenkron model ile onay kaydını [IApprovalQueue](../CustomerSupportBot.Application/Ports/Outbound/Persistence/IApprovalQueue.md) içine yazarak anında `ToolResult.Pending` döner. Gerçek işlem yönetici onay verdiğinde `IApprovalExecutionRouter` üzerinden arka planda tetiklenir.

## Hangi amaçla kullanılır`?

- **Bloklamayan (Non-Blocking) HITL:** Kullanıcının sohbet turunun bir insanın ne zaman karar vereceğine bağlı kalarak kilitlenmesini, askıda kalmasını veya 60 saniyelik zaman aşımına uğramasını önlemek.
- **Ambient Kimlik Güvenliği (`CurrentCustomerId`):** Müşteri kimliğini (`customerId`) LLM parametresi olarak almak yerine, `IApprovalContextAccessor` üzerinden doğrulanmış JWT claim bağlamından almak; böylece bir kullanıcının başkasının müşteri numarasını söyleyerek işlem yapmasını engellemek.
- **Preflight Ön Doğrulama:** Yan etkili işlem onaya gönderilmeden önce siparişin gerçekten kullanıcıya ait ve işlem yapılabilir durumda olduğunu (`ValidateOrderActionable`) denetlemek.
- **Canlı Temsilci Eskalasyon Kaydı:** Ajanların çözemediği durumlarda eskalasyon taleplerini [IEscalationSink](../CustomerSupportBot.Application/Ports/Outbound/Persistence/IEscalationSink.md) üzerinden kaydetmek.

## Sorumlulukları

- **Üstlendiği:**
  - Yan etkili araçları `ExecuteWithApprovalGateAsync` ile onay kuyruğuna yazıp pending yanıtı dönmek.
  - Salt-okunur araçlar (`OrderStatus`, `GetLastOrder`, `GetAllOrders`, `ComplaintStatus`, `GetAllComplaints`) için güvenli ambient müşteri kimliği sarmalamalı builder metotları sunmak.
  - Eskalasyon durumunda `RecordEscalationAsync` ile öncelikli temsilci kuyruğuna kayıt açmak.
- **Üstlenmediği:**
  - Onaylanan işlemin gerçek execution mantığını yürütmek (bu Application katmanındaki `ApprovalExecutionRouter` servisindedir).

## Constructor ve Başlatma Mantığı

```csharp
public ApprovalGateService(
    IApprovalQueue approvalQueue,
    IOptions<ApprovalOptions> approvalOptions,
    IEscalationSink escalationSink,
    IApprovalContextAccessor contextAccessor,
    ICustomerSupportToolsService tools,
    EscalationPolicyService escalationPolicy,
    ILogger<ApprovalGateService>? logger = null)
```

### Constructor İçerisinde Yapılan İşler:
- Onay kuyruğu (`_approvalQueue`), onay seçenekleri (`_approvalOptions`), eskalasyon alıcısı (`_escalationSink`), kimlik bağlamı erişicisi (`_contextAccessor`), araçlar servisi (`_tools`), eskalasyon politikası (`_escalationPolicy`) ve loglayıcı (`_logger`) alanları başlatılır.

## Metotlar ve İç Çalışma Mantıkları

### 1. `BuildOrderPlacementTool`
```csharp
public AIFunction BuildOrderPlacementTool()
```
- **Ne işe yarar?:** Çoklu ürün satırı (`OrderLineRequest[]`) içeren yeni sipariş oluşturma aracını üretir.
- **İç Mantığı:** Tek çağrıda birden fazla satır kabul eder (tek onay = tek sipariş prensibi). `ExecuteWithApprovalGateAsync` çağrısıyla `lines` ve doğrulanmış `CurrentCustomerId` bilgilerini onay kuyruğuna aktarır.

### 2. `BuildOrderCancelTool`
```csharp
public AIFunction BuildOrderCancelTool()
```
- **Ne işe yarar?:** Sipariş iptal aracını (`order_cancel_tool`) üretir.
- **İç Mantığı:** Öncesinde `preflight: () => _tools.ValidateOrderActionable(orderId, CurrentCustomerId)` çalıştırarak siparişin mevcut ve iptal edilebilir durumda olduğunu doğrular; ardından onay kaydı oluşturur.

### 3. `BuildReturnRequestTool`
```csharp
public AIFunction BuildReturnRequestTool()
```
- **Ne işe yarar?:** Sipariş iade talep aracını (`return_request_tool`) üretir.
- **İç Mantığı:** `orderId` ve `reason` parametrelerini doğrular, preflight kontrolünden sonra onay kaydı açar.

### 4. `BuildComplaintRegistrationTool`
```csharp
public AIFunction BuildComplaintRegistrationTool()
```
- **Ne işe yarar?:** Müşteri şikayet kayıt aracını (`complaint_registration_tool`) üretir.
- **İç Mantığı:** `orderId` ve `complaintText` parametrelerini alır, preflight sipariş doğrulamasından sonra onay kaydını kuyruğa yazar.

### 5. Salt-Okunur Araç Builder Metotları
- `BuildOrderStatusTool()`: Sipariş durumunu sorgular (`order_status_tool`).
- `BuildGetLastOrderTool()`: Giriş yapmış müşterinin son siparişini getirir (`get_last_order_tool`).
- `BuildGetAllOrdersTool()`: Müşterinin tüm siparişlerini listeler (`get_all_orders_tool`).
- `BuildComplaintStatusTool()`: Şikayet durumunu sorgular (`complaint_status_tool`).
- `BuildGetAllComplaintsTool()`: Müşterinin tüm şikayetlerini listeler (`get_all_complaints_tool`).

### 6. `ExecuteWithApprovalGateAsync` (Private)
```csharp
private async Task<object> ExecuteWithApprovalGateAsync(
    string toolName,
    Dictionary<string, object?> parameters,
    Func<Task<object>> executeDirect,
    Func<Task>? preflight = null)
```
- **Ne işe yarar?:** Yan etkili araç çağrılarında onay kapısı mantığını işletir.
- **İç Mantığı:**
  1. `preflight` fonksiyonu varsa koşturulur (hata durumunda anında başarısız döner).
  2. Eğer onay sistemi ayarlarda kapalıysa (`_approvalOptions.RequireApproval == false`), `executeDirect()` doğrudan çalıştırılır.
  3. Açık ise `ApprovalRequest` nesnesi oluşturulup `_approvalQueue.EnqueueAsync` ile kuyruğa yazılır.
  4. Kullanıcıya gösterilecek `ToolResult.Pending` sonucu (onay ID'si ve bilgilendirme metni) döndürülür.

### 7. `RecordEscalationAsync`
```csharp
public async Task RecordEscalationAsync(EscalationContext ctx)
```
- **Ne işe yarar?:** Canlı temsilciye eskalasyon gerektiğinde `_escalationSink.RecordAsync` çağrısını yaparak eskalasyon kaydını açar.

## Bağımlılıklar

- [IApprovalQueue](../CustomerSupportBot.Application/Ports/Outbound/Persistence/IApprovalQueue.md)
- [IEscalationSink](../CustomerSupportBot.Application/Ports/Outbound/Persistence/IEscalationSink.md)
- [IApprovalContextAccessor](../CustomerSupportBot.Application/Ports/Outbound/IApprovalContextAccessor.md)
- [ICustomerSupportToolsService](../CustomerSupportBot.Application/Ports/Outbound/ICustomerSupportToolsService.md)
- `Microsoft.Extensions.AI.AIFunctionFactory`

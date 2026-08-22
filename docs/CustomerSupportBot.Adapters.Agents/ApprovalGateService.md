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
private async Task<ToolResult> ExecuteWithApprovalGateAsync(
    string toolName,
    Dictionary<string, object?> parameters,
    Func<ToolResult> executeDirectly,
    Func<ToolResult?>? preflight = null)
```
- **Ne işe yarar?:** Yan etkili araç çağrılarında **bloklamayan** onay kapısı mantığını işletir — dönen `ToolResult`, admin kararını beklemez.
- **İç Mantığı:**
  1. `RequiresApproval(toolName)` — `_approvalOptions.Enabled` kapalıysa veya `toolName` `ToolsRequiringApproval` listesinde değilse `executeDirectly()` doğrudan çalıştırılır (onaya hiç girmez).
  2. `preflight` varsa çağrılır; salt-okunur bir ön kontroldür (ör. sipariş gerçekten var mı / login'li müşteriye mi ait) ve `ToolResult?` döner — `null` değilse (yani reddettiyse) onay kaydı hiç OLUŞTURULMADAN o sonuç döner. Bu, baştan başarısız olacağı belli bir talebin admin'in zamanını harcamasını önler; yürütme anındaki asıl kontrolün yerine geçmez, onunla birlikte çalışır.
  3. Aynı `SessionId` + `toolName` + parametre imzasıyla (`BuildParamSignature`) zaten **bekleyen** bir kayıt varsa (LLM tool çağrısını tekrarladıysa) yenisi oluşturulmaz, mevcut kayıt yeniden kullanılır.
  4. Yoksa yeni bir `ApprovalRequest` (`SessionId`, `CustomerId`, `TraceId`, `UserQuery`, `ToolName`, `AgentName`, `Parameters`, `Justification`, `TimeoutSeconds`) oluşturulup `_approvalQueue.CreateAsync` ile kuyruğa yazılır.
  5. **Kararı beklemeden** `ToolResult.Pending(...)` döner — turn burada biter. Gerçek iş (`executeDirectly` içindeki `_tools.XxxTool(...)` çağrısı) BU metodun içinde artık hiç çalışmaz; admin karar verdiğinde `IApprovalExecutionRouter` üzerinden `PostgresApprovalQueue.DecideAsync`/`InMemoryApprovalQueue.DecideAsync` içinden ayrıca tetiklenir.

> 🐞 **Neden bu şekilde:** Daha önce iki bloklayan model denendi (tool lambda'sının içinde çıplak `await`; ve `ApprovalRequiredAIFunction` + `RequestInfoEvent` ile workflow superstep duraklaması) — ikisi de kullanıcının turu bir insanın karar vereceği ana bağlıyordu. Onaylar biriktiğinde (50-100+ eşzamanlı istek) admin `ApprovalOptions.TimeoutSeconds` (varsayılan 60sn) içinde yetişemiyor ve istekler sessizce otomatik red'e düşüyordu. `RequestApprovalAsync`/`RequestInfoEvent` köprüsü kodda hâlâ duruyor (aşağıya bakın) ama bu 4 tool için hiç tetiklenmiyor.

### 7. `RequestApprovalAsync`
```csharp
public async Task<ApprovalDecisionResult> RequestApprovalAsync(
    string toolName, string agentName, IDictionary<string, object?>? parameters,
    string? justification, CancellationToken ct)
```
- **Ne işe yarar?:** Onay kaydı oluşturup admin kararını **bloklayarak bekleyen** eski yol — `WorkflowRunner`, framework'ün `RequestInfoEvent`'ini yakaladığında bu metodu çağırır.
- **Durum:** Kod hâlâ duruyor ama artık hiçbir tool tarafından tetiklenmiyor, çünkü hiçbiri `ApprovalRequiredAIFunction` ile sarılmıyor (bkz. madde 6). Köprü, ileride biri bilerek bir tool'u o modelde sararsa çalışsın diye bilinçli olarak korunuyor — ölü kod değil, kullanılmayan bir yol.
- **İç Mantığı:** Aynı dedup mantığıyla (`BuildParamSignature`) bekleyen kaydı bulur/oluşturur, `_approvalQueue.AwaitDecisionAsync(req.Id, ct)` ile karar gelene kadar bekler, `ApprovalDecisionResult(approved, reason)` döner; iptal edilirse `false` + `RequestCancelled` mesajı döner.

### 8. `ResolveAgentName` (static)
```csharp
public static string ResolveAgentName(string toolName)
```
- **Ne işe yarar?:** `toolName`'i `WellKnown.SideEffectToolOwners` sözlüğünden hangi ajanın sahiplendiğine çevirir (admin panelinde "hangi ajan istiyor" bilgisini göstermek için) — bulunamazsa `"UnknownAgent"` döner. Elle sürdürülen bir switch YOKTUR, tek kaynak `WellKnown.SideEffectToolOwners`'tır.

### 9. `ProcessPendingEscalationsAsync`
```csharp
public async Task ProcessPendingEscalationsAsync(
    ReasoningTrace trace, string userQuery, string finalResponse, CancellationToken ct = default)
```
- **Ne işe yarar?:** `EscalationPolicyService.ProcessPendingEscalationsAsync`'e ince bir sarmalayıcıdır (facade) — asıl eskalasyon kararı/kaydı mantığı Application katmanındaki `EscalationPolicyService`'tedir, bu metot sadece `_escalationPolicy`'ye devreder. `WorkflowRunner`/`TurnFinalizer` turun sonunda çağırır.

### 10. `BuildParamSignature` (Private, static)
```csharp
private static string BuildParamSignature(IReadOnlyDictionary<string, object?> parameters)
```
- **Ne işe yarar?:** Mükerrer onay talebi tespiti için parametrelerin kanonik (JSON tabanlı, anahtar sırasından bağımsız) imzasını üretir.
- **Neden JSON ile:** Eski yöntem (`"anahtar=deger"` parçalarını `|` ile birleştirmek) hem kaçışlama sorunu (`{a:"x|b=y"}` ile `{a:"x", b:"y"}` aynı imzayı üretiyordu → farklı iki talep aynı sayılıp ikincisi hiç yürütülmüyordu) hem kültür sorunu (`ToString()` geçerli kültürü kullanır; Redis/DB'den geri okunan `JsonElement` kültürden bağımsızdır — Türkçe kurulumda `100,5` ile round-trip `100.5` hiç eşleşmiyordu, çok-pod kurulumda mükerrer koruması tam da en gerekli olduğu yerde sessizce devre dışı kalıyordu) taşıyordu. JSON serileştirme ikisini de çözer. Serileştirilemeyen bir değer varsa (`NotSupportedException`) benzersiz bir GUID imzası dönülür — böylece talep yanlışlıkla "mükerrer" sayılıp bastırılmaz.

## Bağımlılıklar

- [IApprovalQueue](../CustomerSupportBot.Application/Ports/Outbound/Persistence/IApprovalQueue.md)
- [IEscalationSink](../CustomerSupportBot.Application/Ports/Outbound/Persistence/IEscalationSink.md)
- [IApprovalContextAccessor](../CustomerSupportBot.Application/Ports/Outbound/IApprovalContextAccessor.md)
- [ICustomerSupportToolsService](../CustomerSupportBot.Application/Ports/Outbound/ICustomerSupportToolsService.md)
- `EscalationPolicyService` (Application katmanı — eskalasyon karar mantığı)
- `Microsoft.Extensions.AI.AIFunctionFactory`

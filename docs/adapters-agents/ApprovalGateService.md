# ApprovalGateService

**Dosya:** `CustomerSupportBot.Adapters.Agents/ApprovalGateService.cs`  
**Yaşam döngüsü:** Singleton

## Ne yapar?

`ApprovalGateService`, **HITL (Human-in-the-Loop)** onay kapısını uygular. Yan etkili tool'lar (sipariş oluşturma, **sipariş iptali**, **iade talebi**, şikayet kaydı) çalışmadan önce bu servis üzerinden geçer ve admin onayı bekler.

**Temel fikir:** LLM bir tool çağırmak istediğinde, o tool önce admin'e "Bu işlemi yapayım mı?" diye sorar. Admin onaylarsa tool gerçekten çalışır; reddederse kullanıcıya "işlem reddedildi" yanıtı döner.

## HITL Akışı

```
OrderAgent: "order_placement_tool çağırıyorum"
    │
    ▼
ApprovalGateService.BuildOrderPlacementTool() ← daha önce oluşturulan tool
    │
    ▼
RequestApprovalAsync(toolName, agentName, params)
    │
    ├─► ApprovalOptions.Enabled = false? → Direkt onayla (geç)
    ├─► Tool approval listesinde yok? → Direkt onayla (geç)
    │
    └─► IApprovalQueue.Create(req)   ← Queue'ya ekle
            │
            ▼
        IApprovalQueue.AwaitDecisionAsync(req.Id) ← Admin kararını bekle
            │
            ├─► Approved = true  → _tools.OrderPlacementTool(...) çağrılır
            └─► Approved = false → ToolResult.ValidationError(...) döner
```

Admin arayüzü (Admin Chat Panel) bu kuyrukta bekleyen istekleri listeler ve `Approve/Reject` butonları gösterir.

## `BuildOrderPlacementTool`

Sipariş oluşturma tool'unu HITL kapısıyla sarmalar ve bir `AIFunction` olarak döndürür. Bu `AIFunction` doğrudan `OrderAgent`'a tool olarak atanır.

**Tool parametreleri:**

| Parametre | Tür | Açıklama |
|-----------|-----|---------|
| `productName` | string | Sipariş verilecek ürünün adı |
| `quantity` | int? | Sipariş adedi |
| `customerId` | string | Müşteri kimlik numarası (zorunlu) |

**Çalışma şekli:**
1. `RequestApprovalAsync` çağrılır.
2. Onay gelirse `_tools.OrderPlacementTool(productName, quantity, customerId)` çalıştırılır.
3. Ret gelirse `ToolResult.ValidationError(...)` döndürülür ve ajan kullanıcıya ret mesajı iletir.

## `BuildOrderCancelTool`

Sipariş iptal tool'unu HITL kapısıyla sarmalar.

**Tool parametreleri:**

| Parametre | Tür | Açıklama |
|-----------|-----|----------|
| `orderId` | string | İptal edilecek sipariş numarası (zorunlu) |
| `reason` | string | İptal sebebi (min 5 karakter, zorunlu) |

**Çalışma şekli:**
1. `RequestApprovalAsync` çağrılır.
2. Onay gelirse `_tools.OrderCancelTool(orderId, reason)` çalıştırılır.
3. Ret gelirse `ToolResult.ValidationError(...)` döndürülür.

## `BuildReturnRequestTool`

İade talebi tool'unu HITL kapısıyla sarmalar.

**Tool parametreleri:**

| Parametre | Tür | Açıklama |
|-----------|-----|----------|
| `orderId` | string | İade talep edilecek sipariş numarası (zorunlu) |
| `reason` | string | İade sebebi (min 5 karakter, zorunlu) |

**Çalışma şekli:**
1. `RequestApprovalAsync` çağrılır.
2. Onay gelirse `_tools.ReturnRequestTool(orderId, reason)` çalıştırılır.
3. Ret gelirse `ToolResult.ValidationError(...)` döndürülür.

## `BuildComplaintRegistrationTool`

Şikayet kayıt tool'unu HITL kapısıyla sarmalar.

**Tool parametreleri:**

| Parametre | Tür | Açıklama |
|-----------|-----|---------|
| `orderId` | string | Şikayetin ilişkili olduğu sipariş numarası (zorunlu) |
| `complaintText` | string | Şikayet açıklaması (min 10 karakter, zorunlu) |
| `customerId` | string? | Müşteri kimlik numarası (opsiyonel) |

## `RequestApprovalAsync` (özel)

HITL isteği oluşturur ve kararı bekler.

```csharp
private async Task<ApprovalDecisionResult> RequestApprovalAsync(
    string toolName,
    string agentName,
    Dictionary<string, object?> parameters,
    CancellationToken ct)
```

**Önemli davranış — duplicate istek önleme:**

Aynı session'da, aynı tool için, aynı parametrelerle zaten bekleyen bir istek varsa yeni bir istek oluşturulmaz; mevcut istek yeniden kullanılır. Bu, ajan aynı tool'u tekrar çağırmaya çalıştığında ikinci bir onay isteğinin oluşmasını engeller.

```csharp
var paramSig = BuildParamSignature(parameters);
var existing = _approvalQueue.GetPending().FirstOrDefault(p =>
    p.SessionId == sid && p.ToolName == toolName && BuildParamSignature(p.Parameters) == paramSig);
```

**İptal edilirse:** `OperationCanceledException` yakalanır ve `ApprovalDecisionResult(false, "İstek iptal edildi")` döner.

## `ProcessPendingEscalations`

Workflow tamamlandıktan sonra `CustomerSupportTeam` tarafından çağrılır. İçeride `EscalationPolicyService.ProcessPendingEscalations(trace, userQuery, finalResponse)` metoduna delege eder.

Bu metod, `needs_escalation` durumundaki trace'leri tespit eder ve escalation sink'e (örn. `IEscalationSink` → Postgres) yazar.

## `ApprovalOptions` yapılandırması

`appsettings.json` → `HumanInTheLoop:` bölümü:

```json
{
  "HumanInTheLoop": {
    "Enabled": true,
    "ToolsRequiringApproval": [
      "order_placement_tool",
      "order_cancel_tool",
      "return_request_tool",
      "complaint_registration_tool"
    ],
    "TimeoutSeconds": 60,
    "AutoApproveOnTimeout": false,
    "EscalationEnabled": true
  }
}
```

| Ayar | Açıklama |
|------|---------|
| `Enabled` | `false` ise tüm tool'lar otomatik onaylanır (geliştirme ortamı için) |
| `ToolsRequiringApproval` | Hangi tool'ların onay gerektirdiği listesi |
| `TimeoutSeconds` | Onay kuyrukta bekleme süresi |
| `AutoApproveOnTimeout` | Timeout'ta otomatik onayla mı, reddet mi |
| `EscalationEnabled` | Timeout/red durumunda eskalasyon oluşturulsun mu |

> **Dikkat:** Production'da `Enabled: false` olmamalı. `Program.cs` başlangıçta bunu kontrol eder ve `LogCritical` yazar.

## Yeni bir tool'u HITL kapısına bağlamak

1. `ApprovalGateService`'e yeni bir `Build___Tool()` metodu ekleyin, mevcut metodları örnek alın.
2. `ApprovalOptions.ToolsRequiringApproval` listesine yeni tool adını ekleyin.
3. `CustomerSupportTeam` constructor'ında ilgili ajana bu yeni tool'u atayın.

## `BuildParamSignature` (özel)

Parametre sözlüğünden deterministik bir imza string'i üretir. Anahtarlar alfabetik sırayla birleştirilir:

```
{ "customerId": "C001", "productName": "Laptop" }
    → "customerId=C001|productName=Laptop"
```

Bu imza, duplicate istek tespitinde ve log'larda kullanılır.

## `ApprovalDecisionResult` record

```csharp
public sealed record ApprovalDecisionResult(bool Approved, string? Reason);
```

- `Approved`: İşlem onaylandı mı?
- `Reason`: Ret nedeni (varsa admin tarafından girilir)

# ApprovalGateService

**Dosya:** `CustomerSupportBot.Adapters.Agents/ApprovalGateService.cs`
**Yaşam döngüsü:** Singleton

## Ne işe yarar?

**HITL (Human-in-the-Loop)** onay kapısını uygular. Yan etkili 4 tool (sipariş oluşturma, sipariş iptali, iade talebi, şikayet kaydı) config'de işaretliyse, `FunctionInvokingChatClient` bu tool'ları gerçekten çalıştırmadan önce durur ve admin onayı bekler.

> 💡 **Analiz notu:** Bir bankadaki "çift imza" sistemi gibi düşün — büyük para transferi için kasiyerin tek başına işlem yapması yetmez, müdür de onaylamalı. Bot sipariş oluşturacaksa admin panelinden bir insan "evet, oluştur" demelidir.

> **Mimari not (önemli):** Onay bekleme mantığı framework'ün **native** mekanizmasına dayanır — eski modelde (bu dosyanın önceki dokümantasyonunda anlatılan) `RequestApprovalAsync` tool lambda'sının **içinde** çağrılıp bloklayan bir `await` ile beklerdi; süreç restart'ında bu bekleme (ve dolayısıyla `TaskCompletionSource`) kaybolurdu. Artık `Build*Tool()` metotları tool'u `ApprovalRequiredAIFunction` ile sarmalar; `FunctionInvokingChatClient` bunu görünce tool'u çalıştırmadan **önce** bir `ToolApprovalRequestContent` üretir, bu da `AIAgentHostExecutor` üzerinden gerçek, checkpoint'lenebilir bir workflow superstep duraklaması olan `RequestInfoEvent`'e dönüşür. `WorkflowRunner.HandleRequestInfoEventAsync` bu event'i yakalayıp `RequestApprovalAsync`'i (artık `public`) çağırır — metodun gövdesi (kuyruk, bekleme, duplicate-istek koruması) değişmedi, yalnızca **çağrıldığı yer** değişti.

## Hangi amaçla kullanılır?

`AgentTeamFactory`, `OrderAgent`/`ComplaintAgent` kurulurken bu servisin `Build*Tool()` metotlarını çağırıp dönen `AIFunction`'ları ilgili ajana tool olarak atar. `WorkflowRunner.HandleRequestInfoEventAsync`, workflow her HITL duraklamasında `RequestApprovalAsync`'i çağırır.

## Sorumlulukları

- 4 yan etkili tool için sarmalayıcı `AIFunction` üretmek (`BuildOrderPlacementTool`, `BuildOrderCancelTool`, `BuildReturnRequestTool`, `BuildComplaintRegistrationTool`) — her biri `ICustomerSupportToolsService`'teki gerçek implementasyona delege eden bir inner `AIFunction` kurar, sonra `WrapIfRequiresApproval` ile (config'e göre) `ApprovalRequiredAIFunction`'a sarmalar.
- `RequestApprovalAsync`: bir onay talebi oluşturmak (veya aynı session+tool+parametre imzalı bekleyen bir talep varsa onu yeniden kullanmak), `IApprovalQueue.AwaitDecisionAsync` ile admin kararını beklemek, sonucu `ApprovalDecisionResult`'a çevirmek.
- `ResolveAgentName(toolName)`: `ToolApprovalRequestContent` yalnızca tool adı taşıdığı için, admin panelinde "hangi ajan istiyor" bilgisini göstermek amacıyla tool→ajan eşlemesi yapmak.
- `ProcessPendingEscalations`: `EscalationPolicyService.ProcessPendingEscalations`'a delege ederek `needs_escalation` durumundaki trace'leri eskalasyon sink'ine yazdırmak.

**Üstlenmediği işler:** Onayın **ne zaman** isteneceğine framework karar verir (bu servis yalnızca isteği kuyruğa koyup bekler); admin arayüzü/SSE/SLA guardian ayrı bileşenlerdir (`IApprovalQueue`, `ApprovalPortService`, `SlaGuardian` — Application katmanı).

## Diğer katman ve bileşenlerle ilişkileri

**Implements:** Yok — somut bir servis sınıfı (arayüz yok, doğrudan enjekte edilir).

**Bağımlılıkları:** `IApprovalQueue` (Application outbound port — onay kuyruğu), `ApprovalOptions` (config), `IEscalationSink`, `IApprovalContextAccessor` (ambient session/trace/query bilgisi), `ICustomerSupportToolsService` (gerçek tool implementasyonları), `EscalationPolicyService`, `ILogger<ApprovalGateService>?`.

**Kimler çağırır:** `AgentTeamFactory` (constructor'da `OrderAgent`/`ComplaintAgent`'a geçirilir; `Team/OrderAgent.cs` ve `Team/ComplaintAgent.cs` `Build*Tool()` metotlarını doğrudan çağırır), `WorkflowRunner.HandleRequestInfoEventAsync` (`RequestApprovalAsync`), `WorkflowRunner`/`TurnFinalizer` (`ProcessPendingEscalations`).

**Kullandığı MAF tipleri:** `Microsoft.Extensions.AI.ApprovalRequiredAIFunction` (saf işaretleyici — `InvokeAsync` doğrudan iç fonksiyona delege eder, gerçek engelleme `FunctionInvokingChatClient`'ta olur).

## Kullanılma nedeni ve tasarım yaklaşımı

Framework'ün native HITL mekanizmasına geçiş, hexagonal mimariyi bilinçli olarak korudu: Application katmanındaki `IApprovalQueue`/SSE/SLA altyapısı **hiç değişmedi** — yalnızca "kim bekliyor" değişti (eskiden tool lambda'sının içindeki bir `Task`, şimdi framework'ün kendi checkpoint'lenebilir superstep duraklaması). Bu sayede MAF-spesifik bridging (`ApprovalRequiredAIFunction`, `RequestInfoEvent`) tamamen bu adapter katmanında kaldı, Application katmanı framework-agnostic kalmaya devam etti.

**Duplicate istek önleme:** Aynı session'da, aynı tool için, aynı parametrelerle zaten bekleyen bir istek varsa yeni bir istek oluşturulmaz — ajan aynı tool'u tekrar çağırmaya çalışırsa (ör. LLM'in retry davranışı) ikinci bir onay isteği admin panelinde tekrar görünmez.

**Kritik detay — red mesajı formatı:** Admin bir onayı reddettiğinde, LLM'e giden tool sonucu JSON zarfı DEĞİL, `FunctionInvokingChatClient`'ın sabit ürettiği düz bir string: `"Tool call invocation rejected. {reason}"`. Bu özelleştirilemez (private metod, hook yok). `order-agent.md`/`complaint-agent.md` promptlarına bu formatı tanıyıp `status="failed"` üretecek özel talimat eklendi (bkz. `docs/adapters-agents/Team/OrderAgent.md`).

## Metotlar / Üyeler

| Üye | Açıklama |
| --- | --- |
| `BuildOrderPlacementTool()` | `order_placement_tool`'u kurar, `productName`/`quantity`/`customerId` alır, HITL gate'inden geçer. |
| `BuildOrderCancelTool()` | `order_cancel_tool`'u kurar, `orderId`/`reason` alır, HITL gate'inden geçer. |
| `BuildReturnRequestTool()` | `return_request_tool`'u kurar, `orderId`/`reason` alır, HITL gate'inden geçer. |
| `BuildComplaintRegistrationTool()` | `complaint_registration_tool`'u kurar, `orderId`/`complaintText`/`customerId?` alır, HITL gate'inden geçer. |
| `WrapIfRequiresApproval(toolName, inner)` (private) | Config'de onay gerekiyorsa `ApprovalRequiredAIFunction` ile sarmalar, değilse tool'u olduğu gibi döner. |
| `RequiresApproval(toolName)` (private) | `ApprovalOptions.Enabled && ToolsRequiringApproval.Contains(toolName)`. |
| `ResolveAgentName(toolName)` (static) | Tool adından ajan adını çözer (`OrderPlacement`/`OrderCancel`/`ReturnRequest` → Order, `ComplaintRegistration` → Complaint, diğer → `"UnknownAgent"`). |
| `RequestApprovalAsync(toolName, agentName, parameters, justification, ct)` | Onay talebi oluşturur/yeniden kullanır, kararı bekler, `ApprovalDecisionResult` döner. İptal edilirse `(false, "İstek iptal edildi")`. `justification` boş gelirse jenerik `"{agent} bu tool'u çağırmak istiyor."` şablonuna düşülür — çağıran (`WorkflowRunner.ResolveApprovalJustification`) normalde PlanningAgent rationale'ını geçer. |
| `ProcessPendingEscalations(trace, userQuery, finalResponse)` | `EscalationPolicyService`'e delege eder. |
| `BuildParamSignature(parameters)` (private static) | Parametre sözlüğünden deterministik, alfabetik sıralı bir imza string'i üretir (duplicate istek tespiti için). |
| `ApprovalDecisionResult` (record) | `(bool Approved, string? Reason)`. |

## Bağımlılıklar

Constructor injection: `IApprovalQueue approvalQueue`, `IOptions<ApprovalOptions> approvalOptions`, `IEscalationSink escalationSink`, `IApprovalContextAccessor contextAccessor`, `ICustomerSupportToolsService tools`, `EscalationPolicyService escalationPolicy`, `ILogger<ApprovalGateService>? logger = null`.

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
| ------ | --------- |
| `Enabled` | `false` ise tüm tool'lar otomatik onaylanır (geliştirme ortamı için) |
| `ToolsRequiringApproval` | Hangi tool'ların onay gerektirdiği listesi |
| `TimeoutSeconds` | Onay kuyrukta bekleme süresi |
| `AutoApproveOnTimeout` | Timeout'ta otomatik onayla mı, reddet mi |
| `EscalationEnabled` | Timeout/red durumunda eskalasyon oluşturulsun mu |

> **Dikkat:** Production'da `Enabled: false` olmamalı.

## Yeni bir tool'u HITL kapısına bağlamak

1. `ApprovalGateService`'e yeni bir `Build___Tool()` metodu ekleyin, mevcut metodları örnek alın.
2. `ApprovalOptions.ToolsRequiringApproval` listesine yeni tool adını ekleyin.
3. `ResolveAgentName` switch'ine yeni tool→ajan eşlemesini ekleyin.
4. İlgili `Team/*Agent.cs` dosyasında yeni tool'u ajana atayın.

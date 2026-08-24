# IApprovalExecutionRouter (+ ApprovalExecutionOutcome)

**Kaynak:** `Ports/Outbound/IApprovalExecutionRouter.cs`
**Implementasyon:** [`ApprovalExecutionRouter`](../../Services/Approval/ApprovalExecutionRouter.md)

## 1. Ne İşe Yarar

Admin bir onay talebini ONAYLADIĞINDA gerçek işi (sipariş iptali, iade, sipariş verme, şikayet
kaydı) tetikleyen dispatcher.

## 2. Hangi Amaçla Kullanılır

[`IApprovalQueue.DecideAsync`](Persistence/IApprovalQueue.md) tarafından çağrılır — tool
çağrısının kendisi artık admin kararını beklemediği için (bkz. `ApprovalGateService`, HITL
bloklamayan model), gerçek yürütme burada, karar anında gerçekleşir.

## 3. Sorumlulukları

- **Üstlendiği:** `ApprovalRequest.ToolName`'e bakıp doğru `ICustomerSupportToolsService`
  metoduna (`OrderPlacementTool`/`ComplaintRegistrationTool`/`OrderCancelTool`/
  `ReturnRequestTool`) parametreleri deserialize edip yönlendirmek.
- **Üstlenmediği:** Onay kararının kendisi ([`IApprovalQueue.DecideAsync`](Persistence/IApprovalQueue.md)'in
  işi) veya kaydın müşteri bildirimi durumu — bu port yalnızca "onaylandıysa gerçek işi yap"
  adımını üstlenir.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Application/Services/Approval/ApprovalExecutionRouter` implemente eder — küçük bir dispatcher
(4 case, reflection kullanmaz); [`ICustomerSupportToolsService`](ICustomerSupportToolsService.md)'i
inject eder.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Bloklamayan HITL modelinin merkezi parçasıdır: eskiden tool çağrısı admin kararını senkron
bekliyordu (`ApprovalRequiredAIFunction`/`RequestInfoEvent`, 60sn timeout); artık tool hemen
"onaya gönderildi" diyip döner ve gerçek iş, admin karar verdiği ANDA bu router üzerinden
tetiklenir — kullanıcı chat'e devam edebilir, sonuç bir bildirim olarak gelir.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `Task<ApprovalExecutionOutcome> ExecuteAsync(ApprovalRequest request, CancellationToken ct = default)` | Onaylanan talebin gerçek işini yürütür. |

**`ApprovalExecutionOutcome(bool Success, string Message)`** — yürütmenin başarı durumu ve
kullanıcıya/loga gösterilecek mesaj.

## 7. Bağımlılıklar

Port arayüzü `CustomerSupportBot.Domain.Model.ApprovalRequest`'e bağımlıdır.

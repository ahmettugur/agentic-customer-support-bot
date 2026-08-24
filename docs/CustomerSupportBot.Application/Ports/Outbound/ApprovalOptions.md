# ApprovalOptions

**Kaynak:** `Ports/Outbound/ApprovalOptions.cs`
**Ayar bölümü:** `appsettings.json` → `"HumanInTheLoop"`

## 1. Ne İşe Yarar

HITL (human-in-the-loop) onay mekanizmasının tüm konfigürasyonunu taşır: hangi tool'ların onay
gerektirdiği, süpürme (sweep) eşikleri, escalation feature flag'i.

## 2. Hangi Amaçla Kullanılır

`ApprovalGateService` (Adapters.Agents) hangi tool çağrılarının onay akışına gireceğine
`ToolsRequiringApproval`'a bakarak karar verir; `StaleApprovalSweepService` gibi periyodik
servisler `StalePendingHours`/`StuckExecutionAfterMinutes` eşiklerini kullanır.

## 3. Sorumlulukları

- **Üstlendiği:** Yalnızca yapılandırma değerlerini taşımak.
- **Üstlenmediği:** Onay kararının yürütülmesi — o [`IApprovalExecutionRouter`](IApprovalExecutionRouter.md)'ın işi.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`ApprovalGateService`, [`IApprovalQueue`](Persistence/IApprovalQueue.md) ve periyodik süpürme
servisi bu options'ı `IOptions<ApprovalOptions>` ile inject eder.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 🐞 **`TimeoutSeconds`/`AutoApproveOnTimeout` artık bloklamayan modelde anlamsızlaşmıştır:**
> tool artık admin kararını **beklemiyor** (bkz. `ApprovalGateService`) — `CreateAsync` ile
> kayıt açılıp hemen dönülüyor. Bunun yerine `StalePendingHours` (varsayılan 72 saat) kadar
> yanıtsız kalan `Pending` kayıtlar periyodik bir sweep servisiyle otomatik reddedilir.
>
> `StuckExecutionAfterMinutes` (varsayılan 15) olmadan HÂLÂ ÇALIŞAN normal bir işlem de askıda
> görünürdü — panel sık yenilendiği için uzun süren her yürütme "deploy/crash oldu, elle
> doğrulayın" uyarısıyla listelenirdi; yanlış alarm uyarının kendisini değersizleştirir.

`Enabled=false` ile tüm HITL akışı bypass edilebilir — tool'lar doğrudan çalışır (eski
davranış), geliştirme/test ortamlarında hızlı iterasyon için kullanışlıdır.

## 6. Metotlar / Üyeler

| Üye | Varsayılan | Açıklama |
|---|---|---|
| `bool Enabled` | `true` | HITL aktif mi. `false` ise tool'lar direkt çalışır. |
| `List<string> ToolsRequiringApproval` | `[OrderPlacement, ComplaintRegistration]` | Onay isteyen tool adları (snake_case). |
| `int TimeoutSeconds` | `60` | (Bloklayan eski modelde) admin karar vermezse otomatik red süresi. |
| `bool AutoApproveOnTimeout` | `false` | Timeout sonrası varsayılan karar. |
| `int StalePendingHours` | `72` | Bu kadar saat yanıtsız kalan `Pending` kayıtlar sweep ile otomatik reddedilir. |
| `int StuckExecutionAfterMinutes` | `15` | Onaylanmış ama yürütmesi bu kadar dakikadır süren iş "askıda" sayılır. |
| `bool EscalationEnabled` | `true` | Escalation sink feature flag'i. |

## 7. Bağımlılıklar

Port `CustomerSupportBot.Domain.Model.WellKnown.ToolNames` sabitlerine bağımlıdır (varsayılan
liste için).

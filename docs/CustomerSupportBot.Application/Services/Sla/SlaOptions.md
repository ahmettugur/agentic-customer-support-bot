# SlaOptions (+ ApprovalSlaOptions, EscalationSlaOptions, SlaBreachAction)

- **Kaynak:** `Services/Sla/SlaOptions.cs`
- **Namespace:** `CustomerSupportBot.Application.Services.Sla`

## 1. Ne İşe Yarar

SLA / "Response Time Guardian" konfigürasyonu — HITL onay kuyruğunda veya açık
eskalasyonlarda uzun süre bekleyen kayıtlar için uyarı ve ihlal eşiklerini tanımlar.
[`SlaPortService`](SlaPortService.md) bu eşikleri periyodik taramada kullanır.

## 2. Hangi Amaçla Kullanılır

Bekleyen onay/eskalasyon kayıtlarının ne kadar süre "normal" sayılacağını, ne zaman admin
paneline uyarı rozeti düşeceğini ve ihlal (`breach`) sonrası ne yapılacağını
(`SlaBreachAction`) appsettings üzerinden ayarlanabilir kılmak.

## 3. Sorumlulukları

Yalnızca veri taşır — tarama/karar mantığı [`SlaPolicyEvaluator`](SlaPolicyEvaluator.md)'da,
tarama orkestrasyonu [`SlaPortService`](SlaPortService.md)'de.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IOptionsMonitor<SlaOptions>` olarak [`SlaPortService`](SlaPortService.md)'e enjekte edilir
  (`IOptionsMonitor` kullanılması, appsettings'in **yeniden başlatmadan** güncellenebilmesini
  sağlar).
- [`SlaPolicyEvaluator`](SlaPolicyEvaluator.md) — `ApprovalSlaOptions`/`EscalationSlaOptions`'ı
  girdi olarak alır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

### `ApprovalSlaOptions.OnBreach` neden varsayılan `None`, `AutoReject` değil

> 🐞 **Geçmiş tasarım hatası:** HITL bloklamayan modele taşınmadan önce (eski tasarımda) bu
> değer `AutoReject` idi ve o dönem `ApprovalOptions.TimeoutSeconds` (o zamanki tool
> çağrısını `AwaitDecisionAsync` ile bekleten, 60sn) ile aynı tutulması gereken, ikinci/yedek
> bir uygulama katmanıydı. Tool çağrıları artık admin kararını beklemiyor (bkz.
> `ApprovalGateService.ExecuteWithApprovalGateAsync`) — kayıt, admin karar verene ya da
> `ApprovalOptions.StalePendingHours` (varsayılan 72 saat) aşılana kadar kuyrukta kalmalı.
> Tasarım değişirken bu alan güncellenmeyi unutulmuştu: `BreachAfterSeconds=60` +
> `OnBreach=AutoReject` kombinasyonu, her bekleyen onayı admin bakmasa bile 60. saniyede
> **sessizce reddediyordu** — bloklamayan modelin "admin ne zaman bakarsa baksın" amacını
> fiilen geçersiz kılıyordu. `AutoReject`/`AutoApprove` hâlâ bir seçenek olarak duruyor (ör.
> çok agresif bir operasyon politikası isteniyorsa) ama artık varsayılan değil ve kasıtlı bir
> operatör kararı gerektirir.

## 6. Metotlar / Üyeler

### `SlaOptions`

| Üye | Tip | Varsayılan | Açıklama |
|---|---|---|---|
| `SectionName` | `const string` | `"Sla"` | appsettings.json'daki bölüm adı. |
| `Enabled` | `bool` | `true` | SLA Guardian aktif mi? |
| `PollIntervalSeconds` | `int` | `5` | Tarama frekansı. |
| `Approvals` | `ApprovalSlaOptions` | `new()` | Onay kuyruğu eşikleri. |
| `Escalations` | `EscalationSlaOptions` | `new()` | Eskalasyon eşikleri. |

### `ApprovalSlaOptions`

| Üye | Tip | Varsayılan | Açıklama |
|---|---|---|---|
| `WarnAfterSeconds` | `int` | `20` | Bu süreyi aşan onaylar için `warn` event'i yayınlanır. |
| `BreachAfterSeconds` | `int` | `60` | Bu süreyi aşan onaylar SLA ihlali sayılır. |
| `OnBreach` | `SlaBreachAction` | `None` | Breach sonrası aksiyon — bkz. madde 5. |

### `EscalationSlaOptions`

| Üye | Tip | Varsayılan | Açıklama |
|---|---|---|---|
| `WarnAfterSeconds` | `int` | `60` | Bu süreyi aşan açık eskalasyonlar için `warn` event'i yayınlanır. |
| `BreachAfterSeconds` | `int` | `180` | Bu süreyi aşan eskalasyonlar SLA ihlali sayılır. |
| `BoostPriorityOnBreach` | `bool` | `true` | Breach sonrası önceliği bir kademe yükseltir (Low→Normal→High→Critical, Critical'da sabit kalır). |

### `SlaBreachAction` *(enum)*

`None` (yalnızca event yayınla), `AutoReject`, `AutoApprove`.

## 7. Bağımlılıklar

Yalnızca kendi iç tipleri arasında bağımlılık taşır; dış bir bağımlılığı yoktur.

## Bağlantılar

- [SlaPolicyEvaluator.md](SlaPolicyEvaluator.md), [SlaPortService.md](SlaPortService.md)

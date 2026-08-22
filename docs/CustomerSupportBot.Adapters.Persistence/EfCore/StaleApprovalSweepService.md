# StaleApprovalSweepService

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/EfCore/StaleApprovalSweepService.cs`
- **Tür:** `public sealed class : BackgroundService`
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.EfCore`

## Ne işe yarar?

`StaleApprovalSweepService`, arka planda her 15 dakikada bir periyodik olarak çalışan; `ApprovalOptions.StalePendingHours` (varsayılan: 72 saat) süresini aşmış ve hala `Pending` durumunda bekleyen HITL onay taleplerini otomatik olarak reddeden (`approved = false`, `reason = "stale"`) süpürme servisidir.

## Hangi amaçla kullanılır`?

- Bloklamayan modelde müşterinin sipariş/şikayet onay talebi admin tarafından günlerce yanıtlanmadığında, sistemin sonsuza kadar açık talep tutmasını engellemek.
- Red işlemini doğrudan veritabanını güncelleyerek değil, [IApprovalQueue.DecideAsync](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IApprovalQueue.md) üzerinden yürüterek Redis Pub/Sub, SSE bildirimleri ve olay akışlarının eksiksiz tetiklenmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:**
  - 15 dakikalık aralıklarla (`SweepInterval = 15m`) `ExecuteAsync` döngüsünü işletmek.
  - `_approvals.GetPendingAsync` ile bekleyen onayları listelemek.
  - `RequestedAt < cutoff` olan kayıtları `DecideAsync(req.Id, approved: false, decidedBy: "system", reason: "stale")` ile sonlandırmak.

## Constructor ve Başlatma Mantığı

```csharp
public StaleApprovalSweepService(
    IApprovalQueue approvals,
    IOptions<ApprovalOptions> options,
    ILogger<StaleApprovalSweepService> logger)
```

### Constructor İçerisinde Yapılan İşler:
- `_approvals`: Onay kuyruğu arayüzü saklanır.
- `_options`: `ApprovalOptions` yapılandırması (StalePendingHours vb.) saklanır.
- `_logger`: Günlükleme motoru atanır.

## Metotlar ve İç Çalışma Mantıkları

### 1. `ExecuteAsync` (Protected Override)
- **Ne işe yarar?:** Servis çalıştığı sürece 15 dakikada bir `SweepAsync` metodunu çağırır.

### 2. `SweepAsync` (Private)
```csharp
private async Task SweepAsync(CancellationToken ct)
```
- **Ne işe yarar?:** Zaman aşımına uğramış onayları tespit eder ve reddeder.
- **İç Mantığı:**
  1. `cutoff = DateTime.UtcNow - TimeSpan.FromHours(_options.StalePendingHours)` hesaplanır.
  2. `_approvals.GetPendingAsync` ile liste çekilir.
  3. Süresi geçenler için `_approvals.DecideAsync(..., approved: false, decidedBy: "system", reason: "stale")` çağrılır ve uyarı logu atılır.

## Bağımlılıklar

- `Microsoft.Extensions.Hosting.BackgroundService`
- [IApprovalQueue](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IApprovalQueue.md)
- `CustomerSupportBot.Domain.Model.ApprovalOptions`

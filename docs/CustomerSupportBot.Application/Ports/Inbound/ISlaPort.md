# ISlaPort ve SLA Durum Kayıtları

**Dosya:** `Ports/Inbound/ISlaPort.cs`
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## 1. Ne işe yarar?

SLA (Service Level Agreement) olay listesi, anlık durum özeti sunmak ve periyodik SLA taramasını tetiklemek için primary port.

## 2. Hangi amaçla kullanılır?

Admin panelinin SLA dashboard'u güncel durumu (`GetStatusAsync`) ve son olayları (`GetRecentEvents`) göstermek için; arka plan `IHostedService`'i (SLA guardian worker) her iterasyonda `ScanOnceAsync`'i çağırıp bekleyen onayların/eskalasyonların eşiği aşıp aşmadığını kontrol etmek için kullanır.

## 3. Sorumlulukları

- **Üstlendiği:** SLA durumunu okumak ve tek bir tarama döngüsünü çalıştırmak.
- **Üstlenmediği:** Zamanlamayı (kaç saniyede bir taranacağını) yönetmek — bu, `ScanOnceAsync`'i çağıran `IHostedService`'in işidir; bu port yalnızca "bir kez tara" birimini sunar.

## 4. Diğer katman/bileşenlerle ilişkileri

- Implementasyonu `Services/Sla` altında; `IApprovalQueue`/`IEscalationPort` verilerini okuyarak SLA ihlallerini tespit eder.
- Admin panelindeki SLA sayfası ve arka plan worker'ı (SLA Guardian, Api katmanı) tüketicisidir.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

`ScanOnceAsync(SlaOptions opts, ...)` metodunun ayrı, parametreli bir "tek tur" birimi olarak sunulması, worker'ın zamanlama mantığından (periyodik loop) SLA tarama mantığını ayırır — test edilebilirlik ve tek sorumluluk ilkesi.

## 6. Tipler ve Üyeler

### `SlaApprovalStatus(int PendingCount, int OldestSeconds, int WarnAfterSeconds, int BreachAfterSeconds, string OnBreach, int BreachCountRecent)`
Bekleyen onayların SLA durumu — kaç tane bekliyor, en eskisi kaç saniyedir bekliyor, uyarı/ihlal eşikleri, ihlal anında yapılacak aksiyon, son dönemdeki ihlal sayısı.

### `SlaEscalationStatus(int OpenCount, int OldestSeconds, int WarnAfterSeconds, int BreachAfterSeconds, bool BoostPriorityOnBreach, int BreachCountRecent)`
Açık eskalasyonların SLA durumu — benzer alanlar, `BoostPriorityOnBreach` ihlal anında önceliğin otomatik yükseltilip yükseltilmeyeceğini belirtir.

### `SlaStatusResult(bool Enabled, int PollIntervalSeconds, SlaApprovalStatus Approvals, SlaEscalationStatus Escalations)`
İkisinin birleşik özeti — SLA özelliği etkin mi ve tarama aralığı.

### `ISlaPort`

| Metot | Açıklama |
|---|---|
| `IReadOnlyList<SlaEvent> GetRecentEvents(int count = 100)` | Son N SLA olayı. |
| `Task<SlaStatusResult> GetStatusAsync(CancellationToken ct = default)` | Anlık durum özeti. |
| `Task ScanOnceAsync(SlaOptions opts, CancellationToken ct = default)` | Tek bir SLA tarama döngüsü; arka plan worker'ı tarafından her iterasyonda çağrılır. |

## 7. Bağımlılıklar

`CustomerSupportBot.Application.Services.Sla.SlaOptions`, `CustomerSupportBot.Domain.Model.SlaEvent`.

## Bağlantılar

- [IApprovalPort](IApprovalPort.md), [IEscalationPort](IEscalationPort.md) — SLA'nın izlediği kaynaklar.

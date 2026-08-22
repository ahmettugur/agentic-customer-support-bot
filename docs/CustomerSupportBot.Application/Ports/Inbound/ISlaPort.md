# SlaApprovalStatus

- **Kaynak:** `CustomerSupportBot.Application/Ports/Inbound/ISlaPort.cs`
- **Tür:** `public sealed record`
- **Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## Ne işe yarar?

`SlaApprovalStatus`, <summary> SLA olay listesi, anlık durum özeti ve periyodik tarama için primary (driving) port. </summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`SlaApprovalStatus`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Metotlar ve İç Çalışma Mantıkları

### `SlaEscalationStatus`
```csharp
public sealed record SlaEscalationStatus(
    int OpenCount,
    int OldestSeconds,
    int WarnAfterSeconds,
    int BreachAfterSeconds,
    bool BoostPriorityOnBreach,
    int BreachCountRecent)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `SlaStatusResult`
```csharp
public sealed record SlaStatusResult(
    bool Enabled,
    int PollIntervalSeconds,
    SlaApprovalStatus Approvals,
    SlaEscalationStatus Escalations)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`

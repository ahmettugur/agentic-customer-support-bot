# SlaPortService

- **Kaynak:** `CustomerSupportBot.Application/Services/Sla/SlaPortService.cs`
- **Tür:** `public sealed class : ISlaPort`
- **Namespace:** `CustomerSupportBot.Application.Services.Sla`

## Ne işe yarar?

`SlaPortService`, Application/Services/SlaPortService.cs DRIVING PORT IMPL — ISlaPort → ISlaEventSink + IApprovalQueue + IEscalationSink.

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`SlaPortService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public SlaPortService(ISlaEventSink events,
        IApprovalQueue approvals,
        IEscalationSink escalations,
        IOptionsMonitor<SlaOptions> options)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `GetRecentEvents`
```csharp
public IReadOnlyList<SlaEvent> GetRecentEvents(int count = 100)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `ScanOnceAsync`
```csharp
public async Task ScanOnceAsync(SlaOptions opts, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetStatusAsync`
```csharp
public async Task<SlaStatusResult> GetStatusAsync(CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `ISlaPort`

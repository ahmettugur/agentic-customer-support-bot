# EscalationPortService

- **Kaynak:** `CustomerSupportBot.Application/Services/Escalation/EscalationPortService.cs`
- **Tür:** `public sealed class : IEscalationPort`
- **Namespace:** `CustomerSupportBot.Application.Services.Escalation`

## Ne işe yarar?

`EscalationPortService`, Application/Services/EscalationPortService.cs DRIVING PORT IMPL — IEscalationPort → Eskalasyon yönetimi orkestrasyonu. <summary> Eskalasyon yönetimi driving port implementasyonu. Admin panel (AdminEndpoints) bu sınıfı IEscalationPort olarak kullanır. </summary> Driven port event'lerini driving port'a bridge et

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`EscalationPortService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public EscalationPortService(IEscalationSink escalations,
        ILogger<EscalationPortService> logger)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `Create`
```csharp
public EscalationRequest Create(EscalationRequest request)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetOpen`
```csharp
public IReadOnlyList<EscalationRequest> GetOpen()
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetRecent`
```csharp
public IReadOnlyList<EscalationRequest> GetRecent(int count = 50)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetRecentForAgentAsync`
```csharp
public Task<IReadOnlyList<EscalationRequest>> GetRecentForAgentAsync(
        string agentId, int count = 50, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `Get`
```csharp
public EscalationRequest? Get(string id)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `Decide`
```csharp
public bool Decide(string id, string action, string? assignedTo = null, string? resolution = null)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IEscalationPort`

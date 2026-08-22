# HumanAgentPortService

- **Kaynak:** `CustomerSupportBot.Application/Services/Escalation/HumanAgentPortService.cs`
- **Tür:** `public sealed class : IHumanAgentPort, IDisposable`
- **Namespace:** `CustomerSupportBot.Application.Services.Escalation`

## Ne işe yarar?

`HumanAgentPortService`, Application/Services/HumanAgentPortService.cs DRIVING PORT IMPL — IHumanAgentPort → IHumanAgentRegistry + IEscalationSink.

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`HumanAgentPortService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public HumanAgentPortService(IHumanAgentRegistry agents,
        IEscalationSink escalations,
        ILogger<HumanAgentPortService> logger)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `Dispose`
```csharp
public void Dispose()
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetAllMergedAsync`
```csharp
public async Task<IReadOnlyList<HumanAgent>> GetAllMergedAsync(CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetAgent`
```csharp
public HumanAgent? GetAgent(string id)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `CreateAgent`
```csharp
public HumanAgent CreateAgent(HumanAgent agent)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `UpdateAgent`
```csharp
public HumanAgent? UpdateAgent(string id, HumanAgentInput input)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `DeleteAgent`
```csharp
public bool DeleteAgent(string id)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `IncrementLoad`
```csharp
public bool IncrementLoad(string id)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `DecrementLoad`
```csharp
public bool DecrementLoad(string id)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `RerouteEscalation`
```csharp
public RerouteResult RerouteEscalation(string escalationId, string? agentId, string? reason)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IHumanAgentPort, IDisposable`

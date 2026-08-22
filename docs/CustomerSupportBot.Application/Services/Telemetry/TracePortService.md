# TracePortService

- **Kaynak:** `CustomerSupportBot.Application/Services/Telemetry/TracePortService.cs`
- **Tür:** `public sealed class : ITracePort`
- **Namespace:** `CustomerSupportBot.Application.Services.Telemetry`

## Ne işe yarar?

`TracePortService`, Application/Services/TracePortService.cs DRIVING PORT IMPL — ITracePort → IReasoningTraceStore + ISessionManager.

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`TracePortService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public TracePortService(IReasoningTraceStore traces, ISessionManager sessions)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `GetRecentTraces`
```csharp
public IReadOnlyList<ReasoningTrace> GetRecentTraces(int count = 20)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetTrace`
```csharp
public ReasoningTrace? GetTrace(string traceId)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetTracesBySession`
```csharp
public IReadOnlyList<ReasoningTrace> GetTracesBySession(string sessionId)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetSessionsSummaryAsync`
```csharp
public async Task<IReadOnlyList<TracedSessionSummary>> GetSessionsSummaryAsync(CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetStats`
```csharp
public TraceStatsSummary GetStats()
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `ITracePort`

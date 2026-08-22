# ChatSessionSentimentSnapshot

- **Kaynak:** `CustomerSupportBot.Application/Ports/Inbound/IChatSessionPort.cs`
- **Tür:** `public sealed record`
- **Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## Ne işe yarar?

`ChatSessionSentimentSnapshot`, Application katmanında ilgili iş akışını ve domain kurallarını yürüten temel bileşendir.

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ChatSessionSentimentSnapshot`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Metotlar ve İç Çalışma Mantıkları

### `ChatSessionTakeoverResult`
```csharp
public sealed record ChatSessionTakeoverResult(
    string SessionId,
    string HumanAgent,
    int EscalationsAcknowledged,
    string? ErrorCode = null,
    string? ErrorMessage = null)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `ChatSessionReleaseResult`
```csharp
public sealed record ChatSessionReleaseResult(
    string SessionId,
    int EscalationsResolved,
    string? ErrorCode = null,
    string? ErrorMessage = null)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `ChatSessionMessageResult`
```csharp
public sealed record ChatSessionMessageResult(
    string SessionId,
    string? ErrorCode = null,
    string? ErrorMessage = null)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `ChatSessionReplanResult`
```csharp
public sealed record ChatSessionReplanResult(
    string SessionId,
    string? EscalationId,
    string RequestedBy,
    int EscalationsResolved,
    bool ReleasedFromHuman,
    string? ErrorCode = null,
    string? ErrorMessage = null)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`

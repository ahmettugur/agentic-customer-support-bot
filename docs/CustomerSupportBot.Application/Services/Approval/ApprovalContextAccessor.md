# ApprovalContextAccessor

- **Kaynak:** `CustomerSupportBot.Application/Services/Approval/ApprovalContextAccessor.cs`
- **Tür:** `public sealed class : IApprovalContextAccessor`
- **Namespace:** `CustomerSupportBot.Application.Services.Approval`

## Ne işe yarar?

`ApprovalContextAccessor`, <summary> AsyncLocal tabanlı IApprovalContextAccessor implementasyonu. Her async akış kendi bağlamını taşır — paralel workflow'lar izoledir. </summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ApprovalContextAccessor`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Metotlar ve İç Çalışma Mantıkları

### `SetScope`
```csharp
public IDisposable SetScope(string? sessionId, string? traceId, string? userQuery, string? customerId = null)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `SetCurrentAgent`
```csharp
public void SetCurrentAgent(string? agentName)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `SetTraceId`
```csharp
public void SetTraceId(string? traceId)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `Dispose`
```csharp
public void Dispose()
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IApprovalContextAccessor`

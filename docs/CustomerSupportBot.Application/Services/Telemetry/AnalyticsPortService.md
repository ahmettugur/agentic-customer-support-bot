# AnalyticsPortService

- **Kaynak:** `CustomerSupportBot.Application/Services/Telemetry/AnalyticsPortService.cs`
- **Tür:** `public sealed class : IAnalyticsPort`
- **Namespace:** `CustomerSupportBot.Application.Services.Telemetry`

## Ne işe yarar?

`AnalyticsPortService`, Application/Services/AnalyticsPortService.cs DRIVING PORT IMPL — IAnalyticsPort → Analitik veri orkestrasyonu. <summary> Analitik verileri driving port implementasyonu. AnalyticsEndpoints bu sınıfı IAnalyticsPort olarak kullanır. </summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`AnalyticsPortService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public AnalyticsPortService(IRatingStore ratings,
        ISessionManager sessions,
        IApprovalQueue approvals,
        IEscalationSink escalations,
        ILogger<AnalyticsPortService> logger)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `Rate`
```csharp
public ConversationRating Rate(string sessionId, int stars, string? comment = null)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetRating`
```csharp
public ConversationRating? GetRating(string sessionId)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetRecentRatings`
```csharp
public IReadOnlyList<ConversationRating> GetRecentRatings(int count = 20)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetAllRatings`
```csharp
public IReadOnlyList<ConversationRating> GetAllRatings()
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetSummaryAsync`
```csharp
public async Task<object> GetSummaryAsync(CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetDashboardAsync`
```csharp
public async Task<AnalyticsDashboard> GetDashboardAsync(CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetSessionAnalyticsAsync`
```csharp
public async Task<SessionAnalytics?> GetSessionAnalyticsAsync(string sessionId, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IAnalyticsPort`

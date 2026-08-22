# ApprovalPortService

- **Kaynak:** `CustomerSupportBot.Application/Services/Approval/ApprovalPortService.cs`
- **Tür:** `public sealed class : IApprovalPort`
- **Namespace:** `CustomerSupportBot.Application.Services.Approval`

## Ne işe yarar?

`ApprovalPortService`, Application/Services/ApprovalPortService.cs DRIVING PORT IMPL — IApprovalPort → HITL onay kuyruğu orkestrasyonu. <summary> HITL onay akışı driving port implementasyonu. Admin panel (AgentPanelEndpoints) bu sınıfı IApprovalPort olarak kullanır. </summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ApprovalPortService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public ApprovalPortService(IApprovalQueue approvalQueue,
        ICustomerRepository customers,
        ILogger<ApprovalPortService> logger)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `GetPendingAsync`
```csharp
public async Task<IReadOnlyList<ApprovalRequest>> GetPendingAsync(CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetRecentAsync`
```csharp
public async Task<IReadOnlyList<ApprovalRequest>> GetRecentAsync(int count = 50, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetStuckExecutionsAsync`
```csharp
public async Task<IReadOnlyList<ApprovalRequest>> GetStuckExecutionsAsync(CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `Get`
```csharp
public ApprovalRequest? Get(string id)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `DecideAsync`
```csharp
public async Task<bool> DecideAsync(string id, bool approved, string? decidedBy = null, string? reason = null, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IApprovalPort`

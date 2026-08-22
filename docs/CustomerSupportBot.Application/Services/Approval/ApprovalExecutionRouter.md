# ApprovalExecutionRouter

- **Kaynak:** `CustomerSupportBot.Application/Services/Approval/ApprovalExecutionRouter.cs`
- **Tür:** `public sealed class : IApprovalExecutionRouter`
- **Namespace:** `CustomerSupportBot.Application.Services.Approval`

## Ne işe yarar?

`ApprovalExecutionRouter`, Services/Approval/ApprovalExecutionRouter.cs ToolName -> ICustomerSupportToolsService yönlendirmesi. ApprovalRequest.Parameters, ApprovalGateService tarafında Dictionary<string,object?> olarak yazılır ama Postgres'ten hydrate edildiğinde (JSON round-trip) değerler JsonElement olarak gelir — GetString/GetLines her iki kaynağı da (canlı obje veya JsonElement) doğru okur.

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ApprovalExecutionRouter`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public ApprovalExecutionRouter(ICustomerSupportToolsService tools)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `ExecuteAsync`
```csharp
public Task<ApprovalExecutionOutcome> ExecuteAsync(ApprovalRequest request, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IApprovalExecutionRouter`

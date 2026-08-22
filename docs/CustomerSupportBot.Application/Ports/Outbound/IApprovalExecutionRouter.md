# IApprovalExecutionRouter

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/IApprovalExecutionRouter.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound`

## Ne işe yarar?

`IApprovalExecutionRouter`, Ports/Outbound/IApprovalExecutionRouter.cs Admin bir onay talebini ONAYLADIĞINDA gerçek işi (sipariş iptali, iade vb.) tetikleyen dispatcher. IApprovalQueue.DecideAsync tarafından çağrılır — tool çağrısının kendisi artık bu kararı beklemediği için (bkz. ApprovalGateService) gerçek yürütme burada, karar anında gerçekleşir.

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IApprovalExecutionRouter`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Metotlar ve İç Çalışma Mantıkları

### `ApprovalExecutionOutcome`
```csharp
public sealed record ApprovalExecutionOutcome(bool Success, string Message)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`

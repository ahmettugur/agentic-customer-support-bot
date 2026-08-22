# IApprovalContextAccessor

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/IApprovalContextAccessor.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound`

## Ne işe yarar?

`IApprovalContextAccessor`, <summary> Approval akışı boyunca taşınan ambient bağlama erişim için secondary (driven) port. Application servisleri bağlamı set eder; HITL adaptörü (ApprovalGateService) okur. </summary> <summary> Mevcut ambient bağlamda şu an fiilen çalışan uzman ajanın adını günceller (ör. "ProductAgent"). Tool çağrıları (ör. IUiHintEmitter.Emit) bu değeri okuyarak ürettikleri event'i doğru ajana etiketler — stream event zamanlamasına/sırasına bağlı kalmadan. </summary> <summary> Workflow trace oluşturulduktan sonra mevcut ambient scope'a trace kimliğini bağlar. Onay kayıtları bu değeri audit korelasyonu için kullanır. </summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IApprovalContextAccessor`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Metotlar ve İç Çalışma Mantıkları

### `ApprovalContext`
```csharp
public sealed record ApprovalContext(
    string? SessionId, string? TraceId, string? UserQuery, string? AgentName = null, string? CustomerId = null)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`

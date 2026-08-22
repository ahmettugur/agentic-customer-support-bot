# EscalationPolicyService

- **Kaynak:** `CustomerSupportBot.Application/Services/Escalation/EscalationPolicyService.cs`
- **Tür:** `public  class`
- **Namespace:** `CustomerSupportBot.Application.Services.Escalation`

## Ne işe yarar?

`EscalationPolicyService`, <summary> Eskalasyon iş politikası servisi. Dedup, öncelik yükseltme ve skills-based routing kararlarını içerir. Adapter'dan bağımsız, Application katmanında konumlanır. </summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`EscalationPolicyService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public EscalationPolicyService(IEscalationSink escalationSink,
        IOptions<ApprovalOptions> options,
        ISkillsBasedRouter? router = null,
        IHumanAgentRegistry? agentRegistry = null,
        ICustomerProfileStore? profileStore = null,
        ISessionManager? sessionManager = null,
        ILogger<EscalationPolicyService>? logger = null)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `ProcessPendingEscalationsAsync`
```csharp
public async Task ProcessPendingEscalationsAsync(
        ReasoningTrace trace, string userQuery, string finalResponse, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`

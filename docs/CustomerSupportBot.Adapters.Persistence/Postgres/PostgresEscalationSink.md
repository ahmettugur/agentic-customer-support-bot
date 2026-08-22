# PostgresEscalationSink

**Dosya:** `Postgres/PostgresEscalationSink.cs`
**Namespace:** `CustomerSupportBot.Adapters.Persistence.Postgres`
**Port:** [`IEscalationSink`](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IEscalationSink.md)

## 1. Ne İşe Yarar

İnsan temsilciye devir (escalation) taleplerini `hitl.escalations` tablosunda, [`PostgresApprovalQueue`](PostgresApprovalQueue.md) ile aynı hibrit cache + Redis pub/sub mimarisiyle saklar ve durum makinesi (`EscalationStateFactory`) üzerinden `Open → Acknowledged → Resolved/Dismissed` geçişlerini yönetir.

## 2. Hangi Amaçla Kullanılır

Bir ajan kullanıcının talebini karşılayamadığında (`EscalationRequest`) admin/agent paneline düşen kaydı burası oluşturur (`Create`); panel tarafı `Decide` ile devralır/çözer/reddeder.

## 3. Sorumlulukları

- Üstlendiği: eskalasyon CRUD'u, durum geçiş kurallarının `EscalationStateFactory` aracılığıyla uygulanması, cross-pod cache senkronu.
- Üstlenmediği: durum geçişinin kurallarının kendisi (bu [`EscalationStates`](../../CustomerSupportBot.Domain/Services/EscalationStates.md) — Domain katmanı — tarafından tanımlanır, bu sınıf yalnızca çağırır).

## 4. İlişkiler

- `IEscalationSink` portunu implemente eder.
- `IMessageBusPort` (Redis `csbot:escalation:created`/`csbot:escalation:decided`), `IDbContextFactory<CustomerSupportDbContext>` enjekte edilir.
- `RequestCreated`/`RequestDecided` event'leri HITL bildirim akışınca dinlenir.

## 5. Tasarım Yaklaşımı

[`PostgresApprovalQueue`](PostgresApprovalQueue.md) ile aynı "process-içi cache + DB + Redis" üçlü deseni kullanır, ama onay kuyruğundan farklı olarak DB yazımı `ExecuteUpdateAsync` ile koşullu değil, klasik oku-değiştir-kaydet (`UpdateAsync`) ile yapılır — çünkü çift-karar riski burada yok: durum geçişleri `EscalationStateFactory` üzerinden CACHE'teki nesne üzerinde denetleniyor ve `Decide` çağrısı distributed lock kullanmıyor (onay kuyruğundaki kadar kritik bir çifte-yürütme riski taşımıyor, en kötü ihtimalle durum bir kez yanlış geçer, para/stok kaybı olmaz).

> 🐞 **`GetRecentForAgentAsync` neden cache'e değil DB'ye sorar:** Cache yalnızca açık kayıtlar + son `HydrateRecentCount` (500) kapalı kaydı tutar; bir agent'ın kendi eski kapalı eskalasyonu bu pencerenin gerisinde kalıp cache'te hiç görünmeyebilir. Hem `AssignedTo` filtresi hem limit tek bir SQL sorgusunda uygulanır.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `EscalationRequest Create(EscalationRequest request)` | DB INSERT + cache + `RequestCreated` event + Redis yayını (senkron, içeride `.GetAwaiter().GetResult()`). |
| `IReadOnlyList<EscalationRequest> GetOpen()` | Cache'ten `Open`/`Acknowledged` durumundakiler. |
| `Task<IReadOnlyList<EscalationRequest>> GetRecentForAgentAsync(string agentId, int count, CancellationToken ct)` | DB'den, ajana atanmış veya atanmamış kayıtlar. |
| `IReadOnlyList<EscalationRequest> GetRecent(int count = 50)` | Cache'ten en son N kayıt. |
| `EscalationRequest? Get(string id)` | Cache'ten tek kayıt. |
| `bool Decide(string id, string action, string? assignedTo, string? resolution)` | `action` → `Acknowledge`/`Resolve`/`Dismiss`; `EscalationStateFactory.Create(req.Status)` ile geçişi dener, başarılıysa DB UPDATE + event + Redis yayını. |
| `event EventHandler<EscalationRequest>? RequestCreated / RequestDecided` | — |

## 7. Bağımlılıklar

- `IDbContextFactory<CustomerSupportDbContext>`
- `IMessageBusPort`
- `ILogger<PostgresEscalationSink>`

## Bağlantılar

- [IEscalationSink](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IEscalationSink.md)
- [EscalationStates](../../CustomerSupportBot.Domain/Services/EscalationStates.md)
- [PostgresApprovalQueue](PostgresApprovalQueue.md) — aynı mimari desen

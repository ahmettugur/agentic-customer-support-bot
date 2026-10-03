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

[`PostgresApprovalQueue`](PostgresApprovalQueue.md) ile aynı "process-içi cache + DB + Redis" üçlü desenini kullanır. Eşzamanlılık garantileri cache'ten değil **DB'den** gelir, çünkü cache pod başınadır ve pub/sub en-fazla-bir-kez teslimattır:

- **Oluşturma — session + ajan başına tek açık eskalasyon.** `hitl.escalations` üzerindeki unique filtered index (`ux_escalations_open_session_agent`, `status IN ('Open','Acknowledged')`, bkz. [`EscalationConfiguration`](../EfCore/Configurations/Hitl/EscalationConfiguration.md)) yarışın kazananını belirler. [`EscalationPolicyService`](../../CustomerSupportBot.Application/Services/Escalation/EscalationPolicyService.md)'in "önce `GetOpen()`, sonra `CreateAsync`" kontrolü atomik değildir; bileşik sorgunun paralel alt görevleri ya da farklı pod'lar aynı anda kontrolden geçebilir. INSERT kısıta takılırsa (`23505` + index adı) `CreateAsync` **mevcut açık kaydı** döndürür (önce cache'e, yoksa DB'ye bakar) — yeni kayıt, `RequestCreated` event'i ve Redis yayını olmaz. Çağıran, dönen `Id`'yi kendi isteğininkiyle karşılaştırarak kaydın yeni oluşup oluşmadığını anlar.
- **Karar — yarışan iki karardan yalnızca biri uygulanır.** Durum makinesi cache'teki nesnenin **kopyası** üzerinde çalışır; DB'ye koşullu `ExecuteUpdateAsync` (`WHERE id = @id AND status = @beklenen`) yazılır. 0 satır güncellenirse kayıt yeniden okunur: DB'de yoksa (beklenmez) INSERT edilir; durumu başkası değiştirmişse cache DB'nin gerçeğiyle tazelenir ve `DecideAsync` `false` döner. Eskiden state machine paylaşılan cache nesnesini doğrudan değiştiriyor, DB koşulsuz UPDATE ediliyordu: iki admin/pod aynı kaydı aynı anda karara bağladığında ikisi de "başarılı" sayılıyor, sonra yazan öncekinin kararını eziyordu.

> 🐞 **`GetRecentForAgentAsync` neden cache'e değil DB'ye sorar:** Cache yalnızca açık kayıtlar + son `HydrateRecentCount` (500) kapalı kaydı tutar; bir agent'ın kendi eski kapalı eskalasyonu bu pencerenin gerisinde kalıp cache'te hiç görünmeyebilir. Hem `AssignedTo` filtresi hem limit tek bir SQL sorgusunda uygulanır.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Task<EscalationRequest> CreateAsync(EscalationRequest request)` | DB INSERT + cache + `RequestCreated` event + Redis yayını. Aynı session + ajan için açık kayıt varsa (dedup kısıtı) **mevcut kaydı** döndürür; event/yayın yapılmaz. |
| `IReadOnlyList<EscalationRequest> GetOpen()` | Cache'ten `Open`/`Acknowledged` durumundakiler. |
| `Task<IReadOnlyList<EscalationRequest>> GetRecentForAgentAsync(string agentId, int count, CancellationToken ct)` | DB'den, ajana atanmış veya atanmamış kayıtlar. |
| `IReadOnlyList<EscalationRequest> GetRecent(int count = 50)` | Cache'ten en son N kayıt. |
| `EscalationRequest? Get(string id)` | Cache'ten tek kayıt. |
| `Task<bool> DecideAsync(string id, string action, string? assignedTo, string? resolution)` | `action` → `Acknowledge`/`Resolve`/`Dismiss`; geçişi cache nesnesinin kopyasında dener, DB'ye koşullu UPDATE yazar. Başarılıysa cache + event + Redis yayını; kayıt bu arada değişmişse `false` (cache tazelenir). |
| `event EventHandler<EscalationRequest>? RequestCreated / RequestDecided` | — |

## 7. Bağımlılıklar

- `IDbContextFactory<CustomerSupportDbContext>`
- `IMessageBusPort`
- `ILogger<PostgresEscalationSink>`

## Bağlantılar

- [IEscalationSink](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IEscalationSink.md)
- [EscalationStates](../../CustomerSupportBot.Domain/Services/EscalationStates.md)
- [PostgresApprovalQueue](PostgresApprovalQueue.md) — aynı mimari desen
- [EscalationConfiguration](../EfCore/Configurations/Hitl/EscalationConfiguration.md) — dedup kısıtı

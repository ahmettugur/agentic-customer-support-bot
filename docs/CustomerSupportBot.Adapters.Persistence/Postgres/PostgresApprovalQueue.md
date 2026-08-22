# PostgresApprovalQueue

**Dosya:** `Postgres/PostgresApprovalQueue.cs`
**Namespace:** `CustomerSupportBot.Adapters.Persistence.Postgres`
**Port:** [`IApprovalQueue`](../../CustomerSupportBot.Application/Ports/Outbound/IApprovalQueue.md)

## 1. Ne İşe Yarar

HITL (human-in-the-loop) onay kayıtlarının (sipariş verme, şikayet, iptal, iade gibi riskli tool çağrıları) **çok-pod'lu** bir üretim ortamında tutarlı biçimde saklanmasını, cross-pod yayılmasını ve karar anında ilgili işin (sipariş/şikayet yazma) tetiklenmesini sağlar. `hitl.approvals` tablosu + process-içi `ConcurrentDictionary` cache + Redis pub/sub'ın üçünü birleştiren hibrit bir adaptördür.

## 2. Hangi Amaçla Kullanılır

`ApprovalGateService`'in onay gerektiren her tool çağrısı bu sınıfın `CreateAsync`'ini çağırır; admin panelindeki onay/red işlemi `DecideAsync`'i tetikler. Chat tarafındaki "onaylarım" bildirim ekranı `GetUnseenForSessionAsync`/`GetHistoryForCustomerAsync`/`MarkSeenAsync`'i kullanır. `StaleApprovalSweepService` süresi geçmiş bekleyen kayıtları `GetPending()`+`DecideAsync(approved:false)` ile otomatik reddeder.

## 3. Sorumlulukları

- Üstlendiği: onay kaydı CRUD'u, tek-sahiplenme garantili karar yazımı, karar sonrası gerçek işin (`IApprovalExecutionRouter` üzerinden) tetiklenmesi, cross-pod cache tutarlılığı (Redis pub/sub + DB'yi tek gerçek kaynak sayma).
- Üstlenmediği: onayın GEREKİP gerekmediğine karar vermek (bu `ApprovalGateService`'in işi), gerçek sipariş/şikayet iş mantığı (bu `IApprovalExecutionRouter`'ın arkasındaki tool servislerinin işi — bkz. [`ApprovalGateService`](../../CustomerSupportBot.Adapters.Agents/ApprovalGateService.md)).

## 4. İlişkiler

- `IApprovalQueue` portunu implemente eder.
- `IMessageBusPort` (Redis pub/sub — `csbot:approval:created`/`csbot:approval:decided` kanalları), `IAppDistributedLock`, `IApprovalExecutionRouter`, `IDbContextFactory<CustomerSupportDbContext>`, `IOptions<ApprovalOptions>` enjekte edilir.
- `RequestCreated`/`RequestDecided` event'leri `HitlEventPortService`/SSE bildirim akışı tarafından dinlenir.

## 5. Tasarım Yaklaşımı — Bloklamayan Onay Modeli

> 🐞 **Mimari geçiş:** Bu sınıf eskiden `AwaitDecisionAsync`'in bir HTTP isteğini/stream bağlantısını admin kararına kadar (varsayılan 60sn) senkron bloke ettiği bir modelle çalışıyordu. Artık **bloklamayan**: `CreateAsync` hemen döner ("talebiniz onaya gönderildi"), `DecideAsync` kararla BİRLİKTE gerçek işi de (`IApprovalExecutionRouter.ExecuteAsync`) tetikler ve sonucu SSE/bildirim kanalıyla yayınlar. `AwaitDecisionAsync`/`TaskCompletionSource` altyapısı hâlâ kodda duruyor ama yalnızca kaydı OLUŞTURAN pod'da, o an canlı bir bekleyen varsa anlamlıdır — `TaskCompletionSource` process-içi bir primitif olduğundan hiçbir zaman DB'ye yazılmaz; diğer pod'larda (hydrate/Redis ile öğrenilen kayıtlarda) her zaman `null`'dır ("orphan" kayıt).

> 🐞 **Neden `DecideAsync` içinde DB-koşullu sahiplenme (`ClaimDecisionAsync`) var, distributed lock tek başına yetmiyor mu:** Distributed lock yalnızca AYNI ANDA gelen çağrıları serialize eder; bayat bir cache yüzünden SONRADAN gelen ikinci bir "karar" çağrısının aynı işi (örn. iadeyi) tekrar yürütmesini engellemez. Asıl koruma tek bir `UPDATE hitl.approvals SET ... WHERE Id=@id AND Status='Pending'`dir — 0 satır etkilenirse karar başka bir pod'da zaten verilmiştir, gerçek iş hiç çalıştırılmaz ve bu pod kendini `RefreshFromDbAsync` ile DB'den onarır.

> 🐞 **Neden onaylanan iş "karar" ile "yürütme sonucu" farklı alanlarda tutuluyor (`Status` vs `ExecutionStatus`):** Eskiden tool'un kendi işi reddetse bile (örn. stok yetersiz) kayıt yalnızca "Onaylandı" görünüyordu — insanın onayı ile tool'un fiilen başarılı olması aynı şey değildir. `ExecutionStatus` (`None`/`Running`/`Succeeded`/`Failed`) bu ikisini ayırır; `Running` ayrıca karar-yürütme arasında süreç çökerse kaydın "askıda" (Approved+Running) olarak GÖRÜNÜR kalmasını sağlar (`GetStuckExecutionsAsync`).

> 🐞 **`MarkSeenAsync` neden tüm alanları değil sadece `CustomerSeenAt`'i yazıyor:** Eskiden cache'teki tüm nesneyi (Status, DecidedAt, ExecutionResult dahil) geri yazan genel bir UPDATE vardı. Kararı henüz görmemiş (bayat cache'li) bir pod'a düşen "gördüm" isteği, DB'deki Approved kaydı Pending'e GERİ ÇEVİRİRDİ — bu hem yanlış durum göstergesi hem de kaydı yeniden "sahiplenilebilir" hâle getirip `ClaimDecisionAsync`'in mükerrer-yürütme korumasını delen bir hataydı.

> 🐞 **`HydrateAsync` neden TÜM Pending kayıtları çekiyor, son N değil:** Eskiden (yorum "tüm açık kayıtlar" dese de) yalnızca tarihe göre son `HydrateRecentCount` (200) kayıt çekiliyordu. Yoğun kurulumda 200 yeni kaydın gerisinde kalmış eski bir Pending onay cache'e hiç girmez — admin panelinde görünmez, `StaleApprovalSweepService` onu bulamaz, sonsuza dek askıda kalırdı. Karara bağlanmış kayıtlarda sınır zararsızdır (üzerlerinde artık iş yapılmaz).

> 🐞 **`GetUnseenForSessionAsync`/`GetAsync`/`GetHistoryForCustomerAsync` neden cache'e değil doğrudan DB'ye sorar:** Redis pub/sub en-fazla-bir-kez teslimattır ve mesaj kaybı bilinçli olarak yutulur (bkz. `RedisMessageBusAdapter`) — cache üzerinden okumak bu sorguları kayıp bir mesaja bağımlı kılardı; müşteri kararı hiç göremezdi. Bu yüzden "kalıcı/geçmişe dönük" okumalar DB'den, yalnızca "anlık bildirim" amaçlı olanlar cache'ten yapılır.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Task<ApprovalRequest> CreateAsync(ApprovalRequest request, CancellationToken ct)` | DB'ye INSERT + cache + (varsa canlı bekleyen için) `TaskCompletionSource` + `RequestCreated` event + Redis `csbot:approval:created` yayını. |
| `Task<ApprovalRequest> AwaitDecisionAsync(string id, CancellationToken ct)` | Yalnızca kaydı oluşturan pod'da, canlı bir `Tcs` varsa timeout'a kadar bekler; orphan kayıtta `InvalidOperationException` fırlatır. |
| `Task<bool> DecideAsync(string id, bool approved, string? decidedBy, string? reason, CancellationToken ct)` | Distributed lock + `ClaimDecisionAsync` ile koşullu sahiplenme; onaylandıysa `IApprovalExecutionRouter.ExecuteAsync` ile gerçek işi tetikler, sonucu yazar, `RequestDecided` event'i ve Redis `csbot:approval:decided` yayınını tetikler. |
| `IReadOnlyList<ApprovalRequest> GetPending()` | Cache'ten senkron — admin panel salt-okunur uçları için. |
| `Task<IReadOnlyList<ApprovalRequest>> GetPendingAsync(CancellationToken ct)` | DB'den okur VE cache'i uzlaştırır (Redis mesajı kaçırılmış kayıtlar böylece cache'e girer). |
| `IReadOnlyList<ApprovalRequest> GetRecent(int count = 50)` | Cache'ten en son N kayıt. |
| `ApprovalRequest? Get(string id)` | Cache'ten tek kayıt. |
| `Task<IReadOnlyList<ApprovalRequest>> GetUnseenForSessionAsync(string sessionId, string customerId, CancellationToken ct)` | DB'den, `CustomerSeenAt == null` ve karara bağlanmış kayıtlar — badge/bildirim ekranı. |
| `Task<IReadOnlyList<ApprovalRequest>> GetStuckExecutionsAsync(CancellationToken ct)` | Approved+Running durumunda `StuckExecutionAfterMinutes`'ten eski kayıtlar — operasyonel alarm için. |
| `Task<ApprovalRequest?> GetAsync(string id, CancellationToken ct)` | DB'den tek kayıt (cache'e bakmaz). |
| `Task<bool> MarkSeenAsync(string id, CancellationToken ct)` | Yalnızca `CustomerSeenAt` kolonunu koşullu `ExecuteUpdateAsync` ile yazar. |
| `Task<IReadOnlyList<ApprovalRequest>> GetHistoryForCustomerAsync(string customerId, int count, CancellationToken ct)` | DB'den, müşterinin tüm onay geçmişi. |
| `event EventHandler<ApprovalRequest>? RequestCreated / RequestDecided` | HITL bildirim/SSE altyapısının abone olduğu event'ler. |

## 7. Bağımlılıklar

- `IDbContextFactory<CustomerSupportDbContext>`
- `IOptions<ApprovalOptions>`
- `IMessageBusPort` (Redis pub/sub)
- `IAppDistributedLock`
- `IApprovalExecutionRouter`
- `ILogger<PostgresApprovalQueue>`

## Bağlantılar

- [IApprovalQueue](../../CustomerSupportBot.Application/Ports/Outbound/IApprovalQueue.md)
- [ApprovalGateService](../../CustomerSupportBot.Adapters.Agents/ApprovalGateService.md) — çağıran taraf
- [StockDeduction](StockDeduction.md) — aynı koşullu-atomik-güncelleme deseni

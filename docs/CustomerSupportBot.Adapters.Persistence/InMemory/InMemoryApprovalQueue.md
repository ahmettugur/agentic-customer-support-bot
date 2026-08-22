# InMemoryApprovalQueue

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/InMemory/InMemoryApprovalQueue.cs`
- **Port:** `IApprovalQueue` (`CustomerSupportBot.Application.Ports.Outbound`)
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.InMemory`

## 1. Ne İşe Yarar

HITL (human-in-the-loop) onay kuyruğunun tamamen bellek içi implementasyonudur. Her onay isteği için bir `TaskCompletionSource<ApprovalRequest>` tutulur; `DecideAsync` çağrıldığında bu TCS tetiklenir.

## 2. Hangi Amaçla Kullanıldığı

`PostgresApprovalQueue`'nun (production) test/tek-process karşılığıdır. Onay isteklerinin yaşam döngüsünü (oluştur → bekle/veya hemen pending dön → karar ver → yürüt) yönetir.

## 3. Sorumlulukları

- Onay isteği oluşturma ve `RequestCreated` event'i ile SSE katmanını bilgilendirme (`CreateAsync`).
- Karar bekleme, zaman aşımında otomatik karar (`AwaitDecisionAsync`) — `ApprovalOptions.AutoApproveOnTimeout`'a göre onay veya red.
- Karar verildiğinde (`DecideAsync`), onaylandıysa **gerçek işlemi tetikleme**: `IApprovalExecutionRouter.ExecuteAsync` çağrılır ve sonucu (`ExecutionResult`/`ExecutionStatus`) isteğe yazılır — bu, "hemen pending dön, karar gelince ayrı tetiklenen yürütme" modelinin bellek-içi tarafıdır.
- Çift karara (double-decision) karşı `lock (entry.Lock)` içinde durumu `Pending`'den çıkarma kontrolü.
- Ring-buffer tarzı geçmiş sınırlama (`HistoryCapacity = 200`) — bekleyenler asla silinmez, sadece karara bağlanmış eski kayıtlar düşer.
- Müşteri bazlı "görülmemiş" (`unseen`) onay sonuçlarını sorgulama (`GetUnseenForSessionAsync`) ve görüldü işaretleme (`MarkSeenAsync`).
- Uzun süre `Running` durumunda takılı kalmış yürütmeleri tespit etme (`GetStuckExecutionsAsync`).

**Üstlenmediği:** Kalıcılık, pod'lar arası senkronizasyon (production'da Redis pub/sub ile `PostgresApprovalQueue` bunu çözer — burada gerek yok çünkü zaten tek process).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `PostgresApprovalQueue` (`../Postgres/HitlAndChat.md`) ile aynı `IApprovalQueue` arayüzünü uygular.
- `IApprovalExecutionRouter`'ı constructor injection ile alır — onaylanan işlemi gerçekten yürüten dispatcher (`ApprovalExecutionRouter`, Application katmanı).
- `ApprovalOptions` (zaman aşımı, otomatik-onay davranışı) `IOptions<ApprovalOptions>` ile enjekte edilir.
- `RequestCreated`/`RequestDecided` event'leri SSE/chat-event katmanı tarafından dinlenir (bkz. `HitlEventPortService`, Application katmanı).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Bu sınıf, "onay isteği anında pending döner, admin karar verince arka planda yürütülür" HITL modelinin somut örneğidir — `AwaitDecisionAsync`'in artık **çağrılmasına gerek kalmadan** da `DecideAsync` üzerinden tam akış tamamlanabilir (yürütme karar anında, senkron bir HTTP isteğini beklemeden tetiklenir). `AwaitDecisionAsync` hâlâ var ve zaman aşımı otomasyonu için kullanılıyor, ama chat tool çağrısı artık onu beklemek zorunda değil.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `event RequestCreated` / `event RequestDecided` | SSE/chat-event katmanının abone olduğu bildirimler. |
| `CreateAsync(request, ct)` | Yeni onay isteğini kaydeder, `TimeoutSeconds`'ı `ApprovalOptions`'tan doldurur, `RequestCreated` fırlatır. |
| `AwaitDecisionAsync(id, ct)` | İsteğin TCS'ini bekler; süre dolarsa `AutoApproveOnTimeout`'a göre otomatik `DecideAsync` çağrısı tetiklenir. |
| `DecideAsync(id, approved, decidedBy, reason, ct)` | Kararı işler; onaylandıysa `IApprovalExecutionRouter.ExecuteAsync`'i çalıştırıp sonucu isteğe yazar. |
| `GetPending()` / `GetPendingAsync(ct)` | Bekleyen istekleri döner (senkron/async aynı sonucu verir — tek kaynak zaten bellek). |
| `GetRecent(count)` | Son N isteği (durumdan bağımsız) döner. |
| `Get(id)` / `GetAsync(id, ct)` | Tek isteği getirir. |
| `GetUnseenForSessionAsync(sessionId, customerId, ct)` | Karara bağlanmış ama müşterinin henüz görmediği (`CustomerSeenAt == null`) istekleri döner. |
| `GetStuckExecutionsAsync(ct)` | `ApprovalOptions.StuckExecutionAfterMinutes`'ten uzun süredir `Running` durumunda kalan yürütmeleri bulur. |
| `MarkSeenAsync(id, ct)` | `CustomerSeenAt`'i doldurur. |
| `GetHistoryForCustomerAsync(customerId, count, ct)` | Bir müşterinin tüm onay geçmişini (session'dan bağımsız) döner. |

## 7. Bağımlılıklar

- `IOptions<ApprovalOptions>`
- `IApprovalExecutionRouter`
- `ILogger<InMemoryApprovalQueue>`

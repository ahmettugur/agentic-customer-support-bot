# PostgresReasoningTraceStore

**Dosya:** `Postgres/PostgresReasoningTraceStore.cs`
**Namespace:** `CustomerSupportBot.Adapters.Persistence.Postgres`
**Port:** [`IReasoningTraceStore`](../../CustomerSupportBot.Application/Ports/Outbound/Observability/IReasoningTraceStore.md)

## 1. Ne İşe Yarar

Bir ajan turunun tüm akıl yürütme izini (`ReasoningTrace`: planlama, uzman ajan ziyaretleri, tool çağrıları, final yanıt) `observability.reasoning_traces` tablosunda saklayan, yüksek yazma frekansına dayanıklı hibrit cache adaptörü.

## 2. Hangi Amaçla Kullanılır

`WorkflowRunner` bir tur başladığında `StartTrace`, akış boyunca onlarca kez `Update`, tur bitince `Complete` çağırır. Admin/debug panelindeki "bu turda model ne düşündü" ekranları `Get`/`GetRecent`/`GetBySession`'ı kullanır.

## 3. Sorumlulukları

- Üstlendiği: trace CRUD'u, DB yazma sıklığını kontrol etmek (write-storm önleme), cache boyut sınırlaması, crash sonrası yarım kalmış trace'lerin işaretlenmesi.
- Üstlenmediği: trace içeriğinin üretimi (bu `WorkflowTraceEventProcessor`'ın işi, bkz. Adapters.Agents).

## 4. İlişkiler

- `IReasoningTraceStore` portunu implemente eder.
- `IMessageBusPort` (Redis `csbot:trace:started`/`csbot:trace:completed`), `IDbContextFactory<CustomerSupportDbContext>` enjekte edilir.
- Startup'ta [`PersistenceHydrator`](../EfCore/PersistenceHydrator.md) tarafından `MarkInflightAsErrorOnStartupAsync` çağrılır.

## 5. Tasarım Yaklaşımı

> 🐞 **`Update` neden yalnızca cache'e yazıyor, DB'ye değil:** Bir workflow turu boyunca `Update` onlarca kez çağrılır (her ajan ziyaretinde, her tool çağrısında). Her çağrıda tam trace JSON'unu DB'ye yazmak bir "write storm" oluştururdu. Bunun yerine yalnızca iki düşük-frekanslı nokta DB'ye yazar: `StartTrace` (iskelet kayıt) ve `Complete` (tam anlık görüntü, tek `UPDATE`). Aynı gerekçeyle `Update` Redis'e de YAYINLANMAZ — her çağrıda büyük trace JSON'unu Redis'e basmak aynı sorunu oraya taşırdı.

> 🐞 **Trade-off — process, trace tamamlanmadan çökerse:** Trace DB'de "skeleton" (yalnızca `StartTrace`'in yazdığı alanlar) hâlinde takılı kalır; `CompletedAt` sonsuza dek `null` görünür. `MarkInflightAsErrorOnStartupAsync`, uygulama her başladığında bu yarım kalmış kayıtları tarar ve `Error="terminated_by_restart"` ile kapatır — böylece admin panelinde sonsuza dek "devam ediyor" görünen hayalet trace'ler oluşmaz.

Cache, `ConcurrentQueue<string>` ile tutulan ekleme sırasına göre `_maxCacheCapacity`'yi (varsayılan 500) aşınca en eskiyi atan basit bir FIFO sınırlamasıyla büyümesi kontrol altında tutulur (`TrimCache`).

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `ReasoningTrace StartTrace(string sessionId, string userQuery)` | Yeni trace oluşturur, cache'e ekler, DB'ye iskelet INSERT, Redis'e tam yayın. |
| `void Update(ReasoningTrace trace)` | Yalnızca cache günceller (DB/Redis'e yazmaz). |
| `void Complete(string traceId, string? terminationReason, string? finalResponse, string? error)` | `FinalResponse`'u 2000 karakterde kırpar, DB'ye tam UPDATE, Redis'e tam yayın. |
| `ReasoningTrace? Get(string traceId)` | Önce cache, yoksa DB fallback (eski trace'ler için). |
| `IReadOnlyList<ReasoningTrace> GetRecent(int count = 50)` | Cache'ten en yeni N trace. |
| `IReadOnlyList<ReasoningTrace> GetBySession(string sessionId)` | Cache'ten bir oturuma ait tüm trace'ler. |
| `Task MarkInflightAsErrorOnStartupAsync(CancellationToken ct)` | Başlangıçta `CompletedAt == null` olan tüm kayıtları `process_restart` ile kapatır. |

## 7. Bağımlılıklar

- `IDbContextFactory<CustomerSupportDbContext>`
- `IMessageBusPort`
- `ILogger<PostgresReasoningTraceStore>`
- `int maxCacheCapacity` (varsayılan 500)

## Bağlantılar

- [IReasoningTraceStore](../../CustomerSupportBot.Application/Ports/Outbound/Observability/IReasoningTraceStore.md)
- [PersistenceHydrator](../EfCore/PersistenceHydrator.md) — startup temizliği

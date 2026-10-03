# IReasoningTraceStore

**Kaynak:** `Ports/Outbound/Observability/IReasoningTraceStore.cs`
**İmplementasyonlar:** [`InMemoryReasoningTraceStore`](../../../../CustomerSupportBot.Adapters.Persistence/InMemory/InMemoryReasoningTraceStore.md), [`PostgresReasoningTraceStore`](../../../../CustomerSupportBot.Adapters.Persistence/Postgres/PostgresReasoningTraceStore.md)

## 1. Ne İşe Yarar

Ajan akıl yürütme trace'lerinin (`ReasoningTrace`) gözlemlenebilirlik kaydı için secondary
port. Bir turun başlangıcından bitişine kadar üretilen tüm reasoning/planning/tool-call
adımlarını saklar.

## 2. Hangi Amaçla Kullanılır

`WorkflowRunner` bir tur başladığında `StartTraceAsync` çağırır, tur ilerledikçe `Update` ile
trace'i günceller, tur bittiğinde `Complete` ile kapatır. Admin/debug panelindeki "trace
görüntüleyici" `GetRecent`/`GetBySession`/`Get` ile bu kayıtları okur.

## 3. Sorumlulukları

- **Üstlendiği:** Trace yaşam döngüsü yönetimi (başlat/güncelle/tamamla) ve sorgu erişimi.
- **Üstlenmediği:** Trace içeriğinin ne anlama geldiği — bu Domain katmanındaki
  `ReasoningTrace` modelinin sorumluluğudur, bu port yalnızca depolar.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`InMemoryReasoningTraceStore` (test/tek-pod) ve `PostgresReasoningTraceStore` (prod, çoklu pod)
implemente eder. `ContextPipeline`'ın ürettiği `ContextResult.Parts` de trace'e yazılır —
"model bu turda neyi biliyordu?" sorusunu yanıtlamak için (bkz.
[ContextPipeline.md](../../../Services/Chat/ContextPipeline.md)).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Ayrı bir gözlemlenebilirlik port'u olmasının nedeni: reasoning trace'leri hem canlı debug için
(bir turun neden o cevabı verdiğini anlamak) hem de kalite/regresyon analizi için kullanılır —
iş mantığından bağımsız, saf bir "ne oldu" kaydıdır.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `Task<ReasoningTrace> StartTraceAsync(string sessionId, string query)` | Yeni bir trace başlatır. Her turda çağrıldığı için asenkron. |
| `void Update(ReasoningTrace trace)` | Var olan trace'i günceller (yalnızca cache, I/O yok — senkron kalır). |
| `Task CompleteAsync(string traceId, string? terminationReason = null, string? finalResponse = null, string? error = null)` | Trace'i sonlandırır. Bilinçli olarak `CancellationToken` almaz: iptal/timeout yollarında da çağrılır ve kapanış yazısı iptal edilmemeli. |
| `IReadOnlyList<ReasoningTrace> GetRecent(int count = 50)` | Son N trace. |
| `IReadOnlyList<ReasoningTrace> GetBySession(string sessionId)` | Bir oturuma ait tüm trace'ler. |
| `ReasoningTrace? Get(string traceId)` | Tekil trace. |

## 7. Bağımlılıklar

Port arayüzü `CustomerSupportBot.Domain.Model.ReasoningTrace`'e bağımlıdır.

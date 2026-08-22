# ITracePort ve Trace Özet Kayıtları

**Dosya:** `Ports/Inbound/ITracePort.cs`
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## 1. Ne işe yarar?

Reasoning trace (bir turun tüm akıl yürütme/tool-çağırma kaydı) okuma ve istatistik için primary port — geliştiricinin/admin'in "bu turda bot ne düşündü, hangi tool'ları çağırdı" sorusunu cevaplamasını sağlar.

## 2. Hangi amaçla kullanılır?

Admin panelindeki "trace/debug" sayfası, son trace'leri listelemek, tek bir trace'i incelemek, bir oturumun tüm trace'lerini görmek ve genel istatistikleri (ortalama süre, hata oranı vb.) göstermek için kullanır.

## 3. Sorumlulukları

- **Üstlendiği:** Trace okuma ve istatistik sözleşmesini sunmak.
- **Üstlenmediği:** Trace'in nasıl üretildiği/yazıldığı — bu `WorkflowTraceEventProcessor` (Adapters.Agents) ve ilgili persistence adaptörünün işidir; bu port yalnızca okuma tarafıdır.

## 4. Diğer katman/bileşenlerle ilişkileri

- Implementasyonu, trace'lerin saklandığı persistence adaptörünü (Postgres/InMemory) sarar.
- Admin panelindeki debug/trace sayfası tüketicisidir.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

Reasoning trace mekanizması, LLM tabanlı bir sistemde "neden bu cevabı verdi" sorusuna cevap verebilmek için kritiktir — bu port, o kaydın dışa açılan tek okuma yüzeyidir. `TraceStatsSummary` içindeki `TerminationReasons` sözlüğü, turların ne şekilde sonlandığının dağılımını (ör. kaç tanesi normal TERMINATE, kaç tanesi hata/timeout ile bitti) tek bakışta görmeyi sağlar.

## 6. Tipler ve Üyeler

### `TracedSessionSummary(string SessionId, string Title, int TraceCount, DateTime LastTraceAt, string? LastQuery, int MessageCount)`
Bir oturumun trace özeti — kaç trace'i var, son trace zamanı, son sorgu, mesaj sayısı.

### `TraceStatsSummary(int TotalTraces, int CompletedCount, int ErrorCount, double AvgDurationMs, double AvgIterationCount, IReadOnlyDictionary<string, int> TerminationReasons)`
Genel trace istatistikleri — toplam/tamamlanan/hatalı trace sayısı, ortalama süre ve iterasyon sayısı, sonlanma sebeplerinin dağılımı.

### `ITracePort`

| Metot | Açıklama |
|---|---|
| `IReadOnlyList<ReasoningTrace> GetRecentTraces(int count = 20)` | Son N trace. |
| `ReasoningTrace? GetTrace(string traceId)` | Tek trace. |
| `IReadOnlyList<ReasoningTrace> GetTracesBySession(string sessionId)` | Bir oturumun tüm trace'leri. |
| `Task<IReadOnlyList<TracedSessionSummary>> GetSessionsSummaryAsync(CancellationToken ct = default)` | Oturum bazlı trace özetleri. |
| `TraceStatsSummary GetStats()` | Genel istatistikler. |

## 7. Bağımlılıklar

`CustomerSupportBot.Domain.Model.ReasoningTrace`.

## Bağlantılar

- [WorkflowTraceEventProcessor](../../Adapters.Agents/WorkflowTraceEventProcessor.md) — trace'lerin üretildiği yer.

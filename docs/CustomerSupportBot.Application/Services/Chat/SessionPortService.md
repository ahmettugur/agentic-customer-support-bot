# SessionPortService

**Dosya:** `Services/Chat/SessionPortService.cs`
**Port:** `ISessionPort` (driving/inbound port)
**Namespace:** `CustomerSupportBot.Application.Services.Chat`

## 1. Ne İşe Yarar

`ISessionManager`'ın (Outbound/driven port — kalıcılık) genel amaçlı oturum işlemlerini
`ISessionPort`'a (Inbound/driving port — use case) bağlayan ince bir orkestrasyon katmanı.
HTTP adaptörü (`SessionEndpoints`, Api katmanı) oturumla ilgili tüm işlemler için bu sınıfı kullanır.

## 2. Hangi Amaçla Kullanılır

Oturum oluşturma/getirme, güncelleme, listeleme, geçmiş okuma ve genel amaçlı "konuşma ekle"
işlemleri için Api katmanının çağırdığı port implementasyonu.

## 3. Sorumlulukları

- **Üstlendiği:** `ISessionManager` çağrılarını debug-seviyesinde loglamak, "reasoning turuna
  bağlı olmayan" genel amaçlı `AddExchangeAsync` çağrılarında LLM sinyali olmadığını açıkça
  belirtmek (`signals: null`).
- **Üstlenmediği:** Oturumun kalıcılığı/cache senkronizasyonu (`ISessionManager`
  implementasyonlarında — `PostgresSessionManager` vb.), state çıkarımı mantığı
  (`SessionStateExtractor`, Domain katmanı).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `ISessionPort` port'unu implemente eder.
- **Inject eder:** `ISessionManager`, `ILogger`.
- **Kimin tarafından çağrılır:** Api katmanındaki `SessionEndpoints`.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Bu sınıf kasıtlı olarak inceliğini korur (thin orchestration) — asıl karmaşıklık
`ISessionManager` implementasyonlarında (cache senkronizasyonu, çok-pod dağıtımı, Postgres
kalıcılığı) yaşar. `SessionPortService`'in tek katma değeri, **`AddExchangeAsync`'in bu genel
amaçlı (port üzerinden gelen, herhangi bir reasoning turuna bağlı olmayan) versiyonunun
`signals: null` ile çağrılmasını açıkça belgelemesidir** — [`ChatPortService`](ChatPortService.md)
kendi `AddExchangeAsync` çağrısında gerçek `TurnSignals` (LLM'in ürettiği intent/sentiment)
geçirirken, bu port üzerinden gelen çağrıların böyle bir sinyali yoktur; state çıkarımı bu
durumda kural tabanlı (deterministik) yola düşer.

Hexagonal mimaride bu sınıfın var oluş nedeni: `ISessionManager` bir **driven port** (dışarıya,
kalıcılığa bakan), `ISessionPort` ise bir **driving port** (içeriye, use case'e bakan) —
aradaki bu ince servis, iki port'u birbirine bağlayan somut implementasyondur; Api katmanı
doğrudan `ISessionManager`'ı görmez, yalnızca `ISessionPort`'u görür.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `GetOrCreateSessionAsync(string? sessionId, CancellationToken ct = default): Task<AgentSession>` | Oturumu getirir, yoksa oluşturur. |
| `GetSessionAsync(string sessionId, CancellationToken ct = default): Task<AgentSession?>` | Oturumu getirir; yoksa `null`. |
| `UpdateSessionAsync(AgentSession session, CancellationToken ct = default): Task` | Oturumu kalıcılığa yazar. |
| `GetAllSessionsAsync(string? forCustomerId = null, CancellationToken ct = default): Task<IReadOnlyList<SessionInfo>>` | Tüm oturumları (isteğe bağlı müşteri filtresiyle) listeler. |
| `GetHistoryAsync(string sessionId, CancellationToken ct = default): Task<List<ConversationMessage>>` | Konuşma geçmişini döner. |
| `AddExchangeAsync(string sessionId, string userMessage, string botResponse, CancellationToken ct = default): Task` | Genel amaçlı konuşma ekleme; `signals: null` ile kural tabanlı state çıkarımı tetikler. |
| `ExtractAndUpdateStateAsync(string sessionId, string userMessage, string botResponse, CancellationToken ct = default): Task` | State çıkarımını (intent/duygu) ayrıca tetikler. |
| `MutateStateAsync(string sessionId, Action<SessionState> mutator, CancellationToken ct = default): Task` | Oturum durumunu doğrudan bir mutator delegesiyle günceller (ör. replan bayrakları). |

## 7. Bağımlılıklar (Constructor Injection)

- `ISessionManager` — oturum kalıcılığı ve senkronizasyonu.
- `ILogger<SessionPortService>` — çağrıları debug seviyesinde loglar.

## Bağlantılar

- [ChatPortService.md](ChatPortService.md) — reasoning turu bağlamında oturum işlemleri
- [SessionStateService.md](SessionStateService.md) — sentiment/kalıcılık yardımcıları

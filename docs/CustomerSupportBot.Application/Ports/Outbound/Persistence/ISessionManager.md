# ISessionManager (+ SessionInfo)

**Kaynak:** `Ports/Outbound/Persistence/ISessionManager.cs`
**İmplementasyonlar:** [`InMemorySessionManager`](../../../../CustomerSupportBot.Adapters.Persistence/InMemory/InMemorySessionManager.md), [`PostgresSessionManager`](../../../../CustomerSupportBot.Adapters.Persistence/Postgres/PostgresSessionManager.md)

## 1. Ne İşe Yarar

Oturum (`AgentSession`) ve konuşma geçmişi kalıcılığı için en merkezi secondary port'lardan
biri. `SessionInfo` sidebar/oturum listesi için hafif bir özet kaydıdır.

## 2. Hangi Amaçla Kullanılır

Her chat turu `GetOrCreateAsync` ile oturumu açar, `AddExchangeAsync`/`AppendUserMessageAsync`/
`AppendAssistantMessageAsync` ile geçmişe yazar, `ExtractAndUpdateStateAsync` ile oturum
durumunu (duygu, çıkarılan kimlikler vb.) günceller. Sidebar oturum listesi
`GetAllSessionsAsync` kullanır.

## 3. Sorumlulukları

- **Üstlendiği:** Oturum CRUD'u, konuşma geçmişi ekleme/okuma, state extraction tetikleme,
  **tamamen async** çalışmak (Postgres implementasyonunun sync-over-async blocking'e
  ihtiyaç duymaması için).
- **Üstlenmediği:** State extraction'ın KENDİSİ (kural tabanlı duygu/kimlik çıkarımı) — o
  Domain katmanındaki `SessionStateExtractor`'ın işi; bu port yalnızca tetikler ve saklar.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`InMemorySessionManager` (test/tek-pod) ve `PostgresSessionManager` (prod, çoklu pod — Redis
pub/sub ile cache senkronize, [`IMessageBusPort`](../Messaging/IMessageBusPort.md) kullanır)
implemente eder.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 🐞 **`ReloadAsync` neden `GetOrCreateAsync`'ten ayrı bir metot olarak var:**
> `GetOrCreateAsync` cache-first çalışır ve bu, tur kilidi
> ([`IAppDistributedLock`](../Locking/IAppDistributedLock.md)) ile birleşince bayat okuma
> üretir: kilidi bekleyen çağrı, beklemeye BAŞLAMADAN önce aldığı nesne referansını tutmaya
> devam eder. Bu arada başka bir pod oturumu güncellerse Redis dinleyicisi cache'e **yeni bir
> nesne** koyar (mevcut olanı değiştirmez) — yani bekleyen çağrının elindeki referans sessizce
> eskir. Kimlik bağlama gibi "oku-karar ver-yaz" adımları bu yüzden kilidi aldıktan SONRA
> tazelenmiş bir nesneyle (`ReloadAsync`) çalışmalıdır; aksi hâlde iki pod aynı sahipsiz
> oturumu birbirinden habersiz bağlayabilir.

> 🐞 **`AddExchangeAsync`'in `signals` parametresi:** LLM reasoning'inin ürettiği intent/
> sentiment sinyalleri verilirse kural tabanlı çıkarımın YERİNE geçer. `null` geçmek "LLM
> sinyali yok, kural tabanlı çıkarımı kullan" demektir; bu alanları tur ortasında AYRICA
> yazmak çift sayıma yol açar — bu yüzden ya biri ya diğeri, ikisi birden değil.

> 🐞 **`GetAllSessionsAsync(forCustomerId)` filtresi veri kaynağında uygulanır:** çağıranın
> listeyi aldıktan sonra ayıklamasına bırakılmaz. Uç noktanın filtrelemeyi unutması, bir
> müşterinin diğer TÜM müşterilerin oturum kimliklerini görmesi demektir.

## 6. Metotlar / Üyeler

**`SessionInfo`** — `SessionId`, `Title`, `LastActivity`, `MessageCount` (sidebar özeti).

| Metot | Açıklama |
|---|---|
| `Task<AgentSession> GetOrCreateAsync(string? sessionId, CancellationToken ct = default)` | Oturumu getirir/oluşturur (cache-first). |
| `Task<AgentSession> ReloadAsync(string sessionId, CancellationToken ct = default)` | **Kalıcı depodan** yeniden okur, cache'i tazeler — kilit sonrası kullanılmalı. |
| `Task<AgentSession?> GetAsync(string sessionId, CancellationToken ct = default)` | Var olan oturumu okur (oluşturmaz). |
| `Task UpdateAsync(AgentSession session, CancellationToken ct = default)` | Oturumu kalıcılaştırır. |
| `Task<IReadOnlyList<AgentSession>> GetAllAsync(CancellationToken ct = default)` | Tüm oturumlar. |
| `Task MutateStateAsync(string sessionId, Action<SessionState> mutator, CancellationToken ct = default)` | Oturum durumunu atomik mutasyonla günceller. |
| `Task<List<ConversationMessage>> GetHistoryAsync(string sessionId, CancellationToken ct = default)` | Konuşma geçmişini okur. |
| `Task AddExchangeAsync(string sessionId, string userMessage, string botResponse, TurnSignals? signals = null, CancellationToken ct = default)` | Bir turu geçmişe ekler + state çıkarımını tetikler. |
| `Task AppendAssistantMessageAsync(string sessionId, string text, CancellationToken ct = default)` | Tek bir asistan mesajı ekler (örn. onay sonucu bildirimi). |
| `Task AppendUserMessageAsync(string sessionId, string text, CancellationToken ct = default)` | Tek bir kullanıcı mesajı ekler. |
| `Task ClearSessionAsync(string sessionId, CancellationToken ct = default)` | Oturumu temizler. |
| `Task<List<SessionInfo>> GetAllSessionsAsync(string? forCustomerId = null, CancellationToken ct = default)` | Oturum listesi; `forCustomerId` verilirse yalnızca o müşteriye ait. |
| `Task ExtractAndUpdateStateAsync(string sessionId, string userMessage, string botResponse, CancellationToken ct = default)` | State extraction'ı manuel tetikler. |

## 7. Bağımlılıklar

Port arayüzü `CustomerSupportBot.Domain.Model` altındaki `AgentSession`, `SessionState`,
`ConversationMessage`, `TurnSignals`'a bağımlıdır.

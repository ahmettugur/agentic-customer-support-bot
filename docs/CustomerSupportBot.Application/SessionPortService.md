# SessionPortService

**Dosya:** `Services/Chat/SessionPortService.cs`  
**Implements:** `ISessionPort`  
**Yaşam döngüsü:** Singleton

## Ne yapar?

Session CRUD işlemlerini API katmanına sunar. `ISessionManager` driven port'una ince bir sarmalayıcıdır (thin wrapper). İş mantığı içermez; debug logging ekler.

---

## Constructor bağımlılıkları

| Bağımlılık | Açıklama |
|-----------|---------|
| `ISessionManager` | Session kalıcılık katmanı (Postgres/InMemory) |
| `ILogger<SessionPortService>` | Debug log'ları |

---

## Metodlar

Yalnızca `MutateStateAsync` gerçekten async'tir; geri kalan tümü **senkrondur** (`Async` son eki taşımaz, `Task`/`CancellationToken` almaz).

| Metod | Delegasyon | Açıklama |
|-------|-----------|---------|
| `GetOrCreateSession` | `ISessionManager.GetOrCreate` | SessionId ile session getirir veya yeni oluşturur |
| `GetSession` | `ISessionManager.Get` | Tekil session; yoksa `null` |
| `UpdateSession` | `ISessionManager.Update` | Session state'ini günceller |
| `GetAllSessions` | `ISessionManager.GetAll` | Tüm session listesi |
| `GetHistory` | `ISessionManager.GetHistory` | Session konuşma geçmişi |
| `AddExchange` | `ISessionManager.AddExchangeAsync` | Kullanıcı-bot mesaj çifti ekler; `signals: null` geçer (bu genel amaçlı API'ye bir reasoning turu bağlı değildir, LLM sinyali yok — bkz. `TurnSignals`) |
| `ExtractAndUpdateState` | `ISessionManager.ExtractAndUpdateState` | Session'dan state çıkarır ve günceller |
| `MutateStateAsync(sessionId, Action<SessionState> mutator, CancellationToken ct = default)` | — | Delegate ile state mutasyonu (tek gerçek async metod) |

---

## Session State alanları

```csharp
public class SessionState
{
    string? Intent;
    string? Phase;
    string Sentiment;           // Positive / Neutral / Negative
    int ConsecutiveNegativeTurns;
    bool ForceReplanNextTurn;   // ChatSessionPortService.ReplanSessionAsync tarafından set edilir
    string? ReplanNote;         // Replan sırasında query olarak kullanılacak not
    DateTime? LastActivityAt;
    string? CustomerId;
    string? Language;
}
```

---

## Tasarım notu

`SessionPortService` kasıtlı olarak minimal tutulmuştur. Session üzerinde iş mantığı çalıştıran servisler şunlardır:

- `SessionStateService` — turu kaydetme (`PersistExchangeAsync`) ve sentiment alarm kontrolü (`CheckSentimentAlert`); intent/sentiment'in kendisi artık `SessionStateExtractor`'da (Domain) hesaplanır, bkz. `SessionStateService.md`
- `ChatSessionPortService` — admin panel operasyonları
- `ChatPortService` — chat akışında session kullanımı

`SessionPortService` sadece dışa açık API endpoint'leri için direct delegator rolü üstlenir.

---

## API endpoint'leri

```http
GET /sessions/                  → GetAllSessions
GET /sessions/{sessionId}/messages → GetHistory
GET /sessions/{sessionId}/state    → session state
```

> ⚠️ Bu endpoint'ler (`SessionEndpoints.cs`) `Program.cs`'te **hiçbir `RequireAuthorization` grubuna dahil değildir** — kimlik doğrulaması ve rate limit olmadan herkese açıktır. Detay için [Endpoints-Chat.md](../api/Endpoints-Chat.md).

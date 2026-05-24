# SessionPortService

**Dosya:** `Services/SessionPortService.cs`  
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

| Metod | Delegasyon | Açıklama |
|-------|-----------|---------|
| `GetOrCreateSessionAsync` | `ISessionManager.GetOrCreate` | SessionId ile session getirir veya yeni oluşturur |
| `GetSessionAsync` | `ISessionManager.Get` | Tekil session; yoksa `null` |
| `UpdateSessionAsync` | `ISessionManager.Update` | Session state'ini günceller |
| `GetAllSessionsAsync` | `ISessionManager.GetAll` | Tüm session listesi |
| `GetHistoryAsync` | `ISessionManager.GetHistory` | Session konuşma geçmişi |
| `AddExchangeAsync` | `ISessionManager.AppendExchange` | Kullanıcı-bot mesaj çifti ekler |
| `ExtractAndUpdateStateAsync` | `ISessionManager.ExtractAndUpdateState` | Session'dan state çıkarır ve günceller |
| `MutateStateAsync` | `ISessionManager.MutateState` | Delegate ile state mutasyonu |

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

- `SessionStateService` — intent/sentiment güncelleme ve alert tetikleme
- `ChatSessionPortService` — admin panel operasyonları
- `ChatPortService` — chat akışında session kullanımı

`SessionPortService` sadece dışa açık API endpoint'leri için direct delegator rolü üstlenir.

---

## API endpoint'leri

```http
GET    /sessions               → GetAllSessionsAsync
GET    /sessions/{id}          → GetSessionAsync
GET    /sessions/{id}/history  → GetHistoryAsync
DELETE /sessions/{id}          → (ISessionManager.Delete doğrudan)
```

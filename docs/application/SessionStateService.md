# SessionStateService

**Dosya:** `CustomerSupportBot.Application/Services/SessionStateService.cs`  
**Yaşam döngüsü:** Singleton

## Ne yapar?

`AgentSession` üzerindeki durum güncellemelerini (intent, sentiment, geçmiş kaydetme) tek bir yerde toplar. Transport katmanından (SSE, HTTP) bağımsızdır; `ChatPortService` tarafından her akış sonunda çağrılır.

## Metodlar

### `UpdateSessionIntent`

```csharp
public void UpdateSessionIntent(AgentSession session, string? intent)
```

Reasoning sonucundan gelen `intent` değerini `session.State.CurrentIntent`'e yazar ve session'ı persist eder. `Unknown` veya boş intent gelirse güncelleme yapılmaz — önceki intent korunur.

### `UpdateSessionSentiment`

```csharp
public void UpdateSessionSentiment(AgentSession session, ReasoningResult reasoning)
```

LLM reasoning sonucundaki sentiment'i session state'e yazar.

**Önemli kural:** Reasoning `Neutral` sentiment ve `SentimentScore=0.5` döndürürse güncelleme yapılmaz. Bu, LLM'nin "sentiment tespit etmedi" durumunu ifade eder; önceki state korunur.

**Ardışık negatif sayacı:**

```csharp
if (reasoning.SentimentScore < WellKnown.SentimentThresholds.NegativeThreshold)
    state.ConsecutiveNegativeTurns++;
else
    state.ConsecutiveNegativeTurns = 0;
```

Negatif eşiğin altına düşünce sayaç artır; normal veya pozitif gelince sıfırla. Bu sayaç `CheckSentimentAlert` tarafından kullanılır.

### `PersistExchange`

```csharp
public void PersistExchange(string sessionId, string query, string response, IChatBridge chatBridge)
```

Tamamlanan bir tur (kullanıcı sorusu + bot yanıtı) ikili olarak kaydeder:
1. `ISessionManager.AddExchange` → session geçmişine ekler (sonraki turda `history` olarak gelir)
2. `IChatBridge.RecordBotExchange` → admin live-chat paneline bildirir

Yanıt boşsa (streaming kesildi, hata oluştu) hiçbir şey yapılmaz.

### `CheckSentimentAlert`

```csharp
public SentimentAlertResult CheckSentimentAlert(AgentSession session)
```

`ConsecutiveNegativeTurns >= AutoEscalationConsecutiveNegative` eşiğini kontrol eder.

**`SentimentAlertResult` alanları:**

```csharp
public sealed class SentimentAlertResult
{
    public string? Sentiment { get; init; }         // "negative"
    public double Score { get; init; }               // 0.0 - 1.0
    public int ConsecutiveNegativeTurns { get; init; }
    public string SessionId { get; init; }
    public bool ShouldAlert { get; init; }           // eşik aşıldıysa true
    public string AlertMessage => "Müşteri N tur boyunca olumsuz. Bir temsilci bağlanmalı.";
}
```

`ShouldAlert=true` ise `ChatPortService` `sentimentAlert` StreamEvent'i gönderir; admin paneli bunu görür.

## Eşik değerleri (WellKnown.SentimentThresholds)

| Sabit | Varsayılan | Anlamı |
|-------|-----------|--------|
| `NegativeThreshold` | 0.35 | Bu skorun altı "negatif" kabul edilir |
| `AutoEscalationConsecutiveNegative` | 3 | 3 ardışık negatif tur → alert tetikle |

Bu değerler `WellKnown` sınıfında sabittir; konfigürasyona bağlı değildir. Değiştirmek için `WellKnown` sınıfını güncelleyin.

## `ChatPortService` ile entegrasyon

```csharp
// Her streaming akışında:

// 1. Reasoning tamamlandıktan sonra:
_sessionState.UpdateSessionIntent(session, rr.Intent);
_sessionState.UpdateSessionSentiment(session, rr);

// 2. Workflow tamamlandıktan sonra:
_sessionState.PersistExchange(sessionId, query, fullResponse, _chatBridge);

// 3. Akışın sonunda:
var alert = _sessionState.CheckSentimentAlert(session);
yield return new StreamEvent(StreamEventTypes.SentimentUpdate, { ... });
if (alert.ShouldAlert)
    yield return new StreamEvent(StreamEventTypes.SentimentAlert, { ... });
```

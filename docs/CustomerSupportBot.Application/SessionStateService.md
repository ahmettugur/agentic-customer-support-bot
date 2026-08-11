# SessionStateService

**Dosya:** `Services/Chat/SessionStateService.cs`
**Yaşam döngüsü:** Singleton

## Ne yapar?

`AgentSession` üzerindeki durum güncellemelerini (geçmiş kaydetme, sentiment alarm kontrolü) toplar. Transport katmanından (SSE, HTTP) bağımsızdır; `ChatPortService` tarafından her akış sonunda çağrılır.

> ⚠️ Bu sınıf eskiden `UpdateSessionIntentAsync`/`UpdateSessionSentiment` metotlarıyla intent ve sentiment'i **doğrudan** `session.State`'e yazıyordu. O metotlar **kaldırıldı** — bkz. aşağıdaki "Neden değişti?" bölümü. Intent/sentiment artık bu sınıfta değil, `SessionStateExtractor.ExtractAndApply`'da (Domain katmanı) işlenir; bu sınıf yalnızca reasoning sonucunu oraya taşımak için bir taşıyıcı görevi görür.

## Metodlar

### `PersistExchangeAsync`

```csharp
public async Task PersistExchangeAsync(
    string sessionId, string query, string response,
    IChatBridge chatBridge, TurnSignals? signals = null, CancellationToken ct = default)
```

Tamamlanan bir turu (kullanıcı sorusu + bot yanıtı) kaydeder:
1. `ISessionManager.AddExchangeAsync(sessionId, query, response, signals, ct)` → session geçmişine ekler **ve** `SessionStateExtractor.ExtractAndApply`'ı tetikler (intent/sentiment/phase/`ConsecutiveNegativeTurns` burada, TEK yerde hesaplanır — bkz. `Model-Reasoning.md#turnsignals`).
2. `IChatBridge.RecordBotExchange` → admin live-chat paneline bildirir.

Yanıt boşsa (streaming kesildi, hata oluştu) hiçbir şey yapılmaz — `signals` de dahil olmak üzere o turun state çıkarımı hiç çalışmaz.

`signals`, reasoning sonucundan `TurnSignals.From(reasoningResult)` ile üretilip buraya geçirilir; `null` ise `SessionStateExtractor` kural tabanlı (anahtar kelime) çıkarıma düşer.

### `CheckSentimentAlert`

```csharp
public SentimentAlertResult CheckSentimentAlert(AgentSession session)
```

`ConsecutiveNegativeTurns >= AutoEscalationConsecutiveNegative` eşiğini kontrol eder. Bu sayaç artık **yalnızca** `SessionStateExtractor.ExtractAndApply` tarafından, tur başına tek seferde güncellenir (aşağıya bakınız) — bu metot yalnızca **okur**, hiçbir şey yazmaz.

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

## Eşik değerleri (`WellKnown.SentimentThresholds`)

| Sabit | Varsayılan | Anlamı |
|-------|-----------|--------|
| `NegativeThreshold` | 0.35 | Bu skorun altı "negatif" kabul edilir |
| `AutoEscalationConsecutiveNegative` | 3 | 3 ardışık negatif tur → alert tetikle |

Bu değerler `WellKnown` sınıfında sabittir; konfigürasyona bağlı değildir.

## `ChatPortService` ile entegrasyon

```csharp
// Non-streaming (HandleAsync) ve streaming (HandleStreamAsync) yolları AYNI şekilde:

var reasoningResult = await _reasoning.ReasonAsync(query, session, history, ct);
// ... workflow çalışır, response üretilir ...

// Intent/sentiment turun ortasında YAZILMAZ. Reasoning sonucu bir TAŞIYICIYA (TurnSignals)
// çevrilip turun kapanışına kadar bekletilir:
await _sessionState.PersistExchangeAsync(
    sessionId, query, response, _chatBridge, TurnSignals.From(reasoningResult), ct);

// Akışın sonunda (yalnızca streaming'de — sentiment_update/alert event'leri gerekir):
var alert = _sessionState.CheckSentimentAlert(session);
yield return new StreamEvent(StreamEventTypes.SentimentUpdate, { ... });
if (alert.ShouldAlert)
    yield return new StreamEvent(StreamEventTypes.SentimentAlert, { ... });
```

## Neden değişti? (Çift yazar → tek yazar)

Eskiden akış şöyleydi: `ChatPortService` reasoning bitince `UpdateSessionIntentAsync(session, rr.Intent)` ve `UpdateSessionSentiment(session, rr)` ile intent/sentiment'i **doğrudan** `session.State`'e yazıyordu (turun ORTASI). Hemen ardından `AddExchangeAsync` çağrılıyor, o da `SessionStateExtractor.ExtractAndApply`'ı tetikleyip **aynı alanları kural tabanlı değerlerle bir kez daha** yazıyordu (turun SONU). İki sorun doğuruyordu:

1. **LLM'in kararı her turda sessizce eziliyordu.** Belgelenen davranış "LLM daha doğru, kural tabanlıyı override eder"di; gerçek sıra bunun tam tersiydi.
2. **`ConsecutiveNegativeTurns` tur başına iki kez artıyordu** — iki farklı yer aynı sayacı ayrı ayrı `++` ediyordu. `AutoEscalationConsecutiveNegative = 3` eşiği 3 tur yerine 2 turda aşılıyor, admin paneline *"müşteri 4 tur boyunca olumsuz"* gibi yanlış bir sayı gidiyordu.

Ayrıca non-streaming yol (`HandleAsync`) `UpdateSessionSentiment`'i hiç çağırmıyordu — aynı mesaj hangi endpoint'ten geldiğine göre farklı sayaç davranışı üretiyordu.

Çözüm: türetilmiş alanların **tek yazarı** `SessionStateExtractor.ExtractAndApply` (Domain katmanı) oldu. LLM'in ürettikleri artık state'e doğrudan yazılmıyor, `TurnSignals` kaydıyla girdi olarak taşınıyor. `UpdateSessionIntentAsync`/`UpdateSessionSentiment` kaldırıldı; iki endpoint de artık aynı `PersistExchangeAsync` çağrısından geçtiği için davranış farkı da ortadan kalktı. Detaylı gerekçe ve örnekler: `Services-SessionStateExtractor.md`.

# SessionStateService

**Dosya:** `Services/Chat/SessionStateService.cs`
**Tür:** `public sealed class` (+ yardımcı DTO `SentimentAlertResult`)
**Namespace:** `CustomerSupportBot.Application.Services.Chat`

## 1. Ne İşe Yarar

Oturum durumu iş mantığını (bir konuşmayı kalıcılığa yazmak, duygu-durumu uyarısı olup
olmadığını kontrol etmek) transport katmanından (SSE, WebSocket) bağımsız hale getirir.

## 2. Hangi Amaçla Kullanılır

[`ChatPortService.HandleStreamAsync`](ChatPortService.md) ve Api katmanındaki
`ChatEventOrchestrator` (streaming transport) bu servisi kullanır — ikisi de aynı "turu kaydet,
duygu durumunu kontrol et" mantığını tekrarlamak yerine buraya devreder.

## 3. Sorumlulukları

- **Üstlendiği:** Bir konuşmayı (`query`+`response`) geçmişe yazmak ve `IChatBridge`'e
  bildirmek (`PersistExchangeAsync`); ardışık negatif tur eşiğini kontrol edip uyarı gerekip
  gerekmediğine karar vermek (`CheckSentimentAlert`).
- **Üstlenmediği:** Duygu durumunun NASIL hesaplandığı (bu `SessionStateExtractor`'da, Domain
  katmanında — LLM sinyali veya kural tabanlı), transport'a özgü olay biçimlendirme (SSE/WebSocket
  event şeması Api katmanında kurulur, bu servis sadece `SentimentAlertResult` DTO'sunu döner).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Inject eder:** `ISessionManager`, `ILogger`.
- **Kimin tarafından çağrılır:** [`ChatPortService`](ChatPortService.md) (streaming yol),
  Api katmanındaki `ChatEventOrchestrator`.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 🐞 **Intent/sentiment neden turun KAPANIŞINDA, TEK yerde işlenir.** Eskiden ayrı
> `UpdateSessionIntentAsync`/`UpdateSessionSentiment` metotları vardı ve bunlar tur ORTASINDA
> state'e yazıyordu; hemen ardından `PersistExchangeAsync` → `SessionStateExtractor` aynı
> alanları kural tabanlı değerlerle **bir kez daha** eziyordu. İki somut hata sonucu:
> (1) LLM'in ürettiği (daha isabetli) karar, her turda kural tabanlı çıkarım tarafından
> sessizce eziliyordu; (2) `ConsecutiveNegativeTurns` sayacı tur başına **iki kez** artıyordu
> (biri erken yazımdan, biri geç yazımdan), yani gerçekte 2 negatif tur geçince değil 1 negatif
> tur geçince alarm eşiği aşılıyordu. Düzeltme: LLM'in ürettiği sinyaller artık `TurnSignals`
> olarak sadece bir GİRDİ biçiminde taşınır (yazılmaz), türetilmiş alanların TEK yazarı
> `SessionStateExtractor.ExtractAndApply`'dır — `AddExchangeAsync` içinden, turun kapanışında,
> tam olarak bir kez çağrılır.

`PersistExchangeAsync` yalnızca `response` boş DEĞİLSE yazar — boş bir bot cevabının geçmişe
"boş bir tur" olarak eklenmesi anlamsızdır (ör. bir hata/iptal durumunda).

`CheckSentimentAlert`'teki `lock (session)` kilidi, oturum nesnesinin (bellekte, cache'te
paylaşılan) birden fazla eşzamanlı okuyucu/yazıcı tarafından erişilebilmesine karşı bir
korumadır — okunan alanlar (`Sentiment`, `SentimentScore`, `ConsecutiveNegativeTurns`) tutarlı
bir anlık görüntü (snapshot) olarak alınır, yarı güncellenmiş bir durum okunmaz.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `PersistExchangeAsync(string sessionId, string query, string response, IChatBridge chatBridge, TurnSignals? signals = null, CancellationToken ct = default): Task` | Konuşmayı (yanıt boş değilse) geçmişe yazar ve `IChatBridge.RecordBotExchange` ile bildirir. |
| `CheckSentimentAlert(AgentSession session): SentimentAlertResult` | Ardışık negatif tur sayısını eşikle (`WellKnown.SentimentThresholds.AutoEscalationConsecutiveNegative`) karşılaştırır; aşıldıysa uyarı loglar ve `ShouldAlert=true` döner. |
| `SentimentAlertResult` (DTO) | `Sentiment`, `Score`, `ConsecutiveNegativeTurns`, `SessionId`, `ShouldAlert`, hesaplanan `AlertMessage`. |

## 7. Bağımlılıklar (Constructor Injection)

- `ISessionManager` — oturum kalıcılığı.
- `ILogger<SessionStateService>` — duygu-durumu uyarılarını loglar.

## Bağlantılar

- [ChatPortService.md](ChatPortService.md) — bu servisi streaming yolda çağıran taraf
- [ContextPipeline.md](ContextPipeline.md) — aynı klasördeki bağlam toplama pipeline'ı

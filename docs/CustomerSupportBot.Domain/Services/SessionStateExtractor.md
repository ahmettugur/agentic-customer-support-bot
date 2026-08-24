# SessionStateExtractor

**Dosya:** `Services/SessionStateExtractor.cs`
**Tür:** `public static class`

Bir kullanıcı + bot mesaj çiftinden **session state**'i türetir. Hem `InMemorySessionManager` hem `PostgresSessionManager` aynı mantığı kullanır — **tek doğruluk kaynağı**.

---

## Niye gerekli?

Session state şu alanları içerir:
- `CustomerId` — kullanıcının kendi mesajından/bot yanıtından çıkarılan (LLM'in serbest metinden türettiği, **poisonable**) kimlik — gerçek yetkili kimlik için bkz. `AuthenticatedCustomerId` (JWT'den gelir, bu sınıfın işi değildir)
- `CurrentIntent` — şu anki niyet (`sipariş_sorgulama`, `şikayet`, vb.)
- `TurnCount` — turn sayısı
- `Phase` — Greeting / Inquiry / Action / Resolution
- `Sentiment` + `SentimentScore`
- `ConsecutiveNegativeTurns` — peş peşe negatif sentiment sayısı (otomatik eskalasyon eşiğinin girdisi)
- `CollectedInfo` — extracted entity'ler

Bunlar her turn'de güncellenmeli, ama mesaj içeriğinden **deterministic** (LLM'siz) türetilebilmeli — çünkü LLM her zaman bir karar üretmeyebilir (reasoning çağrısı başarısız olabilir, ya da bazı çağıranlar reasoning'i hiç atlar).

---

## `ExtractAndApply`

```csharp
public static void ExtractAndApply(
    SessionState state,
    string userMessage,
    string botResponse,
    IReadOnlyList<ConversationMessage>? priorHistory = null,
    TurnSignals? llm = null)
```

State'i **in-place** günceller. `PostgresSessionManager`/`InMemorySessionManager`'ın `AddExchangeAsync`'i her turda bu metodu **bir kez** çağırır — bkz. aşağıdaki "Tek yazar" bölümü.

### Yapılan iş

1. **ID çıkarımı** (`IdExtractor` çağrısı)
   - `customer_id` → `state.CustomerId` (kullanıcı mesajında yoksa bot yanıtından, yalnızca hâlâ `null` ise)
   - `order_id` → `CollectedInfo["LastMentionedOrderId"]`
   - `priorHistory` bağlamsız (context'siz) bir sayı çıkarımını (ör. önceki turda *"sipariş numaram 1030"* dendikten sonra bu turda sadece *"1030"* yazılması) önceki turun gerçek bağlamına göre yeniden sınıflandırır.

   > ⚠️ **Buradaki yanlış sınıflandırma kalıcı state'i kirletir.** Ancak `EntityVerifier` ve
   > factual tool'lar müşteri kimliği için `state.CustomerId` kullanmaz; yalnız
   > `AuthenticatedCustomerId` güvenlik sınırıdır. Kök neden ve düzeltme:
   > [`IdExtractor` — `numaram` sahiplenmesi](IdExtractor.md). Regresyon koruması
   > `SessionStateExtractorTests`.
2. **Intent tespiti** — `state.CurrentIntent = llm?.Intent ?? DetectUserIntent(userMessage)`
   - LLM (reasoning) bir intent ürettiyse **o kazanır**; üretmediyse `WellKnown.IntentKeywords` tablosuna düşülür.
   - Kural tabanlı tabloda özel bir kural da var: `"sipariş"` + (`"durum"` veya `"takip"` veya `"nerede"`) → `sipariş_sorgulama`.
3. **Phase belirleme**
   - Turn 1 → `Greeting`
   - Bot mesajında `WellKnown.ResponseKeywords.SuccessMarker` (`"başarıyla"`) varsa → `Resolution`
   - Bot mesajında `MissingInfoMarker` (`"EKSİK_BİLGİ"`) varsa → `Inquiry`
   - Aksi halde `Action`
4. **Sentiment** — aynı öncelik: `llm` hem etiket hem skor içeriyorsa o kullanılır, yoksa `WellKnown.SentimentKeywords` tablosundan match aranır (yoksa `"neutral"` / `0.5`).
5. **Consecutive negative tracking** — az önce (4)'te belirlenen **tek** sentiment sonucuna göre: skor `< 0.35` (NegativeThreshold) ise counter artar, değilse sıfırlanır. Counter `≥ 3` (`AutoEscalationConsecutiveNegative`) olduğunda yukarı katmanda (`SessionStateService.CheckSentimentAlert`) otomatik eskalasyon tetiklenir.

---

## `TurnSignals` — LLM'in girdisi, ikinci bir yazıcı değil

```csharp
public sealed record TurnSignals(string? Intent, string? SentimentLabel, double? SentimentScore);
```

`TurnSignals.From(reasoningResult)` reasoning sonucunu bu tipe süzer (bkz. `Model-Reasoning.md`). Dolu olan her alan kural tabanlı çıkarımın **yerine** geçer; `null` alanlarda (2) ve (4)'teki kural tabanlı yol devreye girer.

**Neden bu şekilde tasarlandı:** Eskiden LLM'in ürettiği intent/sentiment, turun ORTASINDA (`ChatPortService`) doğrudan `session.State`'e yazılıyordu; birkaç satır sonra bu metod (`ExtractAndApply`) aynı alanları kural tabanlı değerlerle **bir kez daha** yazıyordu. İki sonucu vardı:

- LLM'in kararı her turda sessizce eziliyordu — belgelenen "LLM daha doğru, kural tabanlıyı override eder" davranışının **tam tersi** oluyordu.
- `ConsecutiveNegativeTurns` iki farklı yerden artırıldığı için **tur başına iki kez** ilerliyordu; `AutoEscalationConsecutiveNegative = 3` eşiği 3 tur yerine 2 turda aşılıyordu.

Artık `ExtractAndApply`, turun türetilmiş alanlarının **tek yazarıdır**. LLM'in sonucu `TurnSignals` ile buraya **girdi** olarak taşınır; state'e ikinci bir elden asla doğrudan yazılmaz. Çift sayım bu sayede yapısal olarak imkânsız hale gelir.

---

## Akış örneği

```
Turn 3 başlıyor:
  state.TurnCount = 2
  state.CustomerId = "12345"
  state.CurrentIntent = "şikayet"
  state.Phase = "Inquiry"
  state.ConsecutiveNegativeTurns = 1

userMessage = "Berbat bir hizmet, hiçbir şey çalışmıyor!"
botResponse = "Sorununuzu anlıyorum, hemen bir temsilciye bağlıyorum"
llm = null   // reasoning bu turda intent/sentiment üretmemiş (ör. hata/timeout)

ExtractAndApply(state, userMessage, botResponse, priorHistory, llm) sonrası:
  state.TurnCount = 3
  state.CurrentIntent = "şikayet"              ← kural tabanlı tablo ("berbat" eşleşmedi ama önceki intent korunmuyor, tablo "genel"e düşerdi — burada gösterim amaçlı basitleştirildi)
  state.Sentiment = "angry"
  state.SentimentScore = 0.10
  state.ConsecutiveNegativeTurns = 2            ← 1 → 2 (TEK artış, ikinci bir yazardan gelen ek artış yok)
  state.Phase = "Action"                        ← bot temsilci bağlıyor
```

---

## Threshold sabitleri (`WellKnown.SentimentThresholds`)

| Sabit | Değer | Açıklama |
|---|---|---|
| `AngryThreshold` | 0.15 | Bu altı → "angry" sayılır |
| `NegativeThreshold` | 0.35 | Bu altı → "negative" |
| `PositiveThreshold` | 0.65 | Bu üstü → "positive" |
| `AutoEscalationConsecutiveNegative` | 3 | Peş peşe 3 negatif tur → otomatik eskalasyon |

Threshold'lar burada tek yerde — değiştirilmek istenirse `WellKnown.cs` düzenlenir.

---

## Test edilebilirlik

Saf static fonksiyon:

```csharp
var state = new SessionState();
SessionStateExtractor.ExtractAndApply(state, "5 nolu siparişim nerede", "Sipariş kargoda");
Assert.Equal("sipariş_sorgulama", state.CurrentIntent);
Assert.Equal("Action", state.Phase);   // "başarıyla" yok
```

`llm` parametresi opsiyonel olduğu için mevcut testlerin çoğu ona hiç dokunmadan geçer; `TurnSignals` içeren senaryolar ayrıca test edilir (`SessionStateExtractorTests`, `TurnSignalsTests`).

---

## Neden Domain'de?

Bu mantık iki adapter'da da (InMemory + Postgres) çalışır. Adapter'lardan birine koyarsak diğeri kopya tutar veya farklı davranır. **Domain Service** olarak tutmak DRY'ı sağlar ve davranış farkı riskini sıfırlar — `TurnSignals`'ın da Domain'de (Application değil) tanımlı olmasının sebebi budur: her iki adapter da Application katmanına bağımlı olmadan bu tipi kullanabilmeli.

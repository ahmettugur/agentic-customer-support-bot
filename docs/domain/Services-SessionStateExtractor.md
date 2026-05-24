# SessionStateExtractor

**Dosya:** `Services/SessionStateExtractor.cs`  
**Tür:** `public static class`

Bir kullanıcı + bot mesaj çiftinden **session state**'i türetir. Hem `InMemorySessionManager` hem `PostgresSessionManager` aynı mantığı kullanır — **tek doğruluk kaynağı**.

---

## Niye gerekli?

Session state şu alanları içerir:
- `CustomerId` — kullanıcı kimliği
- `CurrentIntent` — şu anki niyet (OrderInquiry, Complaint, vb.)
- `TurnCount` — turn sayısı
- `Phase` — Greeting / Inquiry / Action / Resolution
- `Sentiment` + `SentimentScore`
- `ConsecutiveNegativeTurns` — peş peşe negatif sentiment sayısı
- `CollectedInfo` — extracted entity'ler

Bunlar her turn'de güncellenmeli, ama mesaj içeriğinden **deterministic** (LLM'siz) türetilmeli.

---

## `ExtractAndApply`

```csharp
public static void ExtractAndApply(
    ChatSessionState state,
    string userMessage,
    string botResponse,
    int turnNumber)
```

State'i **in-place** günceller.

### Yapılan iş

1. **ID çıkarımı** (`IdExtractor` çağrısı)
   - `customer_id`, `order_id`, `complaint_id` → `CollectedInfo` Dict
2. **Intent tespiti**
   - `WellKnown.IntentKeywords` tablosundan keyword match
   - Özel kurallar: `"sipariş"` + (`"durum"` veya `"takip"` veya `"nerede"`) → `OrderInquiry`
   - `"yeni sipariş"`, `"satın al"` → `OrderCreation`
3. **Phase belirleme**
   - Turn 1 → `Greeting`
   - Bot mesajında `WellKnown.ResponseKeywords.SuccessMarker` (`"başarıyla"`) varsa → `Resolution`
   - Bot mesajında `MissingInfoMarker` (`"EKSİK_BİLGİ"`) varsa → `Inquiry`
   - Aksi halde `Action`
4. **Sentiment**
   - `WellKnown.SentimentKeywords` tablosundan match
   - Match yoksa → `"neutral"` / `0.5`
5. **Consecutive negative tracking**
   - Sentiment `< 0.35` (NegativeThreshold) ise counter artar
   - Eşit/üstüyse counter sıfırlanır
   - Counter ≥ 3 (`AutoEscalationConsecutiveNegative`) ise auto-escalation tetiklenir (yukarı katmanda)

---

## Keyword tablosu kaynağı

Tüm keyword'ler `WellKnown.cs` içinde:

```csharp
public static readonly IReadOnlyList<(string Intent, string[] Keywords)> IntentKeywords =
[
    (Intents.OrderCreation, new[] { "sipariş ver", "satın al", "ürün al", ... }),
    (Intents.Complaint,     new[] { "şikayet", "memnun değilim", "iade", ... }),
    // ...
];

public static readonly IReadOnlyList<(string Label, double Score, string[] Keywords)> SentimentKeywords =
[
    ("angry",    0.10, new[] { "berbat", "rezalet", "çileden çıkardın", ... }),
    ("negative", 0.25, new[] { "kötü", "memnun değil", "yetersiz", ... }),
    ("positive", 0.85, new[] { "harika", "teşekkür", "süper", ... }),
];
```

Bu tablo Türkçe ifadelere göre düzenlenmiş.

---

## Akış örneği

```
Turn 3 başlıyor:
  state.TurnCount = 2
  state.CustomerId = "12345"
  state.CurrentIntent = "OrderInquiry"
  state.Phase = "Inquiry"
  state.ConsecutiveNegativeTurns = 1

userMessage = "Berbat bir hizmet, hiçbir şey çalışmıyor!"
botResponse = "Sorununuzu anlıyorum, hemen bir temsilciye bağlıyorum"

ExtractAndApply(state, userMessage, botResponse, 3) sonrası:
  state.TurnCount = 3
  state.CurrentIntent = "Complaint"           ← şikayet kelimeleri yok ama "berbat" → sentiment + müşteri öfke modu
  state.Sentiment = "angry"
  state.SentimentScore = 0.10
  state.ConsecutiveNegativeTurns = 2          ← 1 → 2
  state.Phase = "Action"                       ← bot temsilci bağlıyor
```

---

## Threshold sabitleri (`WellKnown.SentimentThresholds`)

| Sabit | Değer | Açıklama |
|---|---|---|
| `AngryThreshold` | 0.15 | Bu altı → "angry" sayılır |
| `NegativeThreshold` | 0.35 | Bu altı → "negative" |
| `PositiveThreshold` | 0.65 | Bu üstü → "positive" |
| `AutoEscalationConsecutiveNegative` | 3 | Peş peşe 3 negatif → otomatik escalation |

Threshold'lar burada tek yerde — değiştirilmek istenirse `WellKnown.cs` düzenlenir.

---

## Test edilebilirlik

Saf static fonksiyon:

```csharp
var state = new ChatSessionState();
SessionStateExtractor.ExtractAndApply(state, "ORD-5 nerede", "Sipariş kargoda", 1);
Assert.Equal("OrderInquiry", state.CurrentIntent);
Assert.Equal("ORD-5", state.CollectedInfo["order_id"]);
Assert.Equal("Resolution", state.Phase);   // "başarıyla" yok ama bot yanıt verdi → kontrol et
```

Burada `ExtractAndApply` çağrısının deterministic olması test edilebilirliği maksimize eder.

---

## Neden Domain'de?

Bu mantık iki adapter'da da (InMemory + Postgres) çalışır. Adapter'lardan birine koyarsak diğeri kopya tutar veya farklı davranır. **Domain Service** olarak tutmak DRY'ı sağlar ve davranış farkı riskini sıfırlar.

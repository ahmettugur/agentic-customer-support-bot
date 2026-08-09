# Session Modelleri

**Dosyalar:**
- `Model/AgentSession.cs`
- `Model/ChatSessionState.cs`
- `Model/ChatMode.cs`
- `Model/ChatBridgeMessage.cs`
- `Model/ConversationMessage.cs`
- `Model/ConversationPhase.cs`

Session ve sohbet ile ilgili tüm core domain modeller.

---

## AgentSession

```csharp
public class AgentSession
{
    public string SessionId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastActivity { get; set; } = DateTime.Now;
    public SessionState State { get; set; } = new();
}
```

Bir kullanıcının **bir oturumu**. `ISessionManager` bu nesnelerin kalıcılığını yönetir.

**Kullanım:**
- Her API çağrısında `SessionId` ile session bulunur (yoksa oluşturulur)
- `LastActivity` her turn'de güncellenir (timeout/cleanup için)
- `State` türetilmiş durum (intent, phase, sentiment, collected info, vb.)

---

## SessionState

Session'ın **türetilmiş durumu** — `SessionStateExtractor` günceller, ajanlar arası paylaşılan bağlam.

```csharp
public class SessionState
{
    // Kimlik & sayaçlar
    public string? CustomerId { get; set; }
    public int TurnCount { get; set; }
    public string? ConversationSummary { get; set; }

    // Niyet & faz
    public string? CurrentIntent { get; set; }
    public string Phase { get; set; } = WellKnown.Phases.Greeting;

    // Toplanan bilgiler (extracted entities)
    public Dictionary<string, string> CollectedInfo { get; set; } = new();

    // Sentiment takibi
    public string Sentiment { get; set; } = WellKnown.Sentiments.Neutral;
    public double SentimentScore { get; set; } = 0.5;
    public List<SentimentEntry> SentimentHistory { get; set; } = new();
    public int ConsecutiveNegativeTurns { get; set; }

    // Manuel müdahale (admin replan)
    public bool ForceReplanNextTurn { get; set; }
    public string? ReplanRequestedBy { get; set; }
    public DateTime? ReplanRequestedAt { get; set; }
    public string? ReplanNote { get; set; }
}
```

`IReplanService.ExecuteAsync` `ForceReplanNextTurn` flag'ini okur, işlem sonrası temizler.

### CollectedInfo örnek

```csharp
state.CollectedInfo = new()
{
    ["customer_id"] = "12345",
    ["order_id"] = "5",
    ["product_name"] = "Dell XPS 15"
};
```

Specialist agent'lar bu Dict'i okur, tool parametresi olarak kullanır.

### SentimentEntry

```csharp
public class SentimentEntry
{
    public int Turn { get; set; }
    public string Label { get; set; } = WellKnown.Sentiments.Neutral;
    public double Score { get; set; } = 0.5;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
```

---

## ChatSessionState

**HITL Live Takeover snapshot'ı** — bir session'ın mod durumunu temsil eder. Admin UI "Aktif Sohbetler" listesinde render edilir.

```csharp
public class ChatSessionState
{
    public string SessionId { get; set; } = "";
    public ChatMode Mode { get; set; } = ChatMode.Bot;
    public string? HumanAgent { get; set; }       // Human modda devralan temsilci
    public DateTime? EnteredAt { get; set; }       // Human moda geçiş anı
    public DateTime? LastActivityAt { get; set; }  // Son mesaj zaman damgası
    public int MessageCount { get; set; }          // Köprüdeki toplam mesaj sayısı
}
```

> ⚠️ `ChatSessionState`, oturum durum bilgilerini (intent, sentiment, collected info) **taşımaz** — bunlar `SessionState` sınıfında tutulur.

---

## ChatMode

Live-takeover modu enum'u:

```csharp
public enum ChatMode { Bot, Human }
```

| Mod | Açıklama |
|---|---|
| `Bot` | Normal workflow — agent ekibi yanıtlıyor |
| `Human` | Canlı takeover — bir insan agent doğrudan kullanıcıyla konuşuyor; bot devre dışı |

`IChatModeRegistry` her session için modu tutar; mode değişiklikleri Redis pub/sub ile broadcast edilir (multi-pod sync).

---

## ChatBridgeMessage

Live chat'te yayılan mesajların ortak formatı:

```csharp
public sealed class ChatBridgeMessage
{
    public string SessionId { get; init; }
    public ChatBridgeSender Sender { get; init; }   // User | Bot | Admin | System | BotTyping
    public string Text { get; init; }
    public DateTime At { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
}

public enum ChatBridgeSender { User, Bot, Admin, System, BotTyping }
```

| Sender | Kaynak |
|---|---|
| `User` | Web client'tan |
| `Bot` | Agent yanıtı |
| `Admin` | Canlı takeover'daki insan agent |
| `System` | Bot/Human geçiş bildirimleri |
| `BotTyping` | Geçici "yazıyor..." işareti — **DB'ye yazılmaz** |

`IChatBridge` (InMemory + Postgres) bu mesajları yayar.

---

## ConversationMessage

Bot'un dahili konuşma geçmişi formatı — LLM'lere geçirilen yapı:

```csharp
public sealed record ConversationMessage(string Role, string Text)
{
    public const string System = "system";
    public const string User = "user";
    public const string Assistant = "assistant";
}
```

`ChatBridgeMessage`'tan farkı:
- ChatBridgeMessage = canlı sohbet UI mesajları
- ConversationMessage = LLM context için ham geçmiş (sistem prompt'u, user query, assistant response)

---

## ConversationPhase

```csharp
public enum ConversationPhase { Greeting, Inquiry, Action, Resolution }
```

| Faz | Tipik turn |
|---|---|
| `Greeting` | Turn 1 — "Merhaba" |
| `Inquiry` | Bilgi toplama — "siparişin numarası ne?" |
| `Action` | Tool çağrısı, işlem yapılıyor |
| `Resolution` | İş bitti — "siparişiniz kargoda" |

`SessionStateExtractor` her turn'de fazı günceller (bkz. [Services-SessionStateExtractor.md](Services-SessionStateExtractor.md)).

`WellKnown.Phases` string sabitlerini içerir — kod string'le karşılaştırırken bunu kullanır.

---

## Bağlantılar

- [Services-SessionStateExtractor.md](Services-SessionStateExtractor.md) — State nasıl güncellenir
- [Model-Hitl.md](Model-Hitl.md) — Human takeover akışı
- [Model-Trace.md](Model-Trace.md) — `SessionAnalytics` bu state'ten zenginleşir

# StreamEvent Ailesi

**Dosya:** `Ports/Inbound/StreamEvent.cs`
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

Bu dosyada `IChatPort.HandleStreamAsync`'in kullandığı streaming event modeli (`StreamEvent`), üç tipli payload kaydı (`SessionEventPayload`, `TextDeltaPayload`, `ResponseCompletePayload`) ve tüm event tip adlarının toplandığı `StreamEventTypes` sabit sınıfı birlikte tanımlıdır.

## 1. Ne işe yarar?

SSE (Server-Sent Events) ile istemciye akan bir chat turunun her adımını (reasoning başladı, delta geldi, tool onay bekliyor, insan devraldı vb.) tek tip bir zarf (`StreamEvent`) içinde taşır.

## 2. Hangi amaçla kullanılır?

`IChatPort.HandleStreamAsync`, `ChatPortService`, `WorkflowResponseExtractor`, `RealtimeBridgeService` gibi bileşenler bu event'leri üretir; Api katmanındaki SSE endpoint'i bunları JSON'a çevirip istemciye yollar; frontend (`chat-bridge.js`) `Type` alanına göre event'i işler.

## 3. Sorumlulukları

- **Üstlendiği:** Tüm streaming event tiplerini tek bir zarfta (`Type` + `Data`) standardize etmek; bilinen event tip adlarını merkezi bir yerde (`StreamEventTypes`) sabitlemek.
- **Üstlenmediği:** Event'lerin ne zaman/nasıl üretileceği — bu iş event'i üreten servislerdedir.

## 4. Diğer katman/bileşenlerle ilişkileri

- `ChatPortService`, `WorkflowResponseExtractor`, `RealtimeBridgeService` (Application/Adapters.Agents) event üretir.
- Api katmanındaki SSE endpoint'i tüketip JSON'a serileştirir.
- `chat-bridge.js` (Web katmanı) `Type` alanına göre dallanır.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

`TextDeltaPayload` ve `ResponseCompletePayload`'ın tipli kayıtlar olarak var olması, geçmişte gerçek bir hatayı kapatmak için eklenmiştir:

> 🐞 **Geçmiş hata:** Bu payload'lar eskiden anonim `new { text = ... }` nesneleriydi. `ResponseCompletePayload` için sunucu tarafında **hiçbir tipli tüketicisi yoktu** — okunamadığı için kimse okumaya çalışmamıştı. Sonuç: `ChatPortService` konuşma geçmişini, `RealtimeBridgeService` ise TTS'e okutulacak metni, event'i okumak yerine delta'ları birleştirerek üretiyordu. Ajan adı sızıntısı olan bir turda ekranda temiz metin görünürken **veritabanına ham metin yazılıyor** ve **sesli kanalda müşteri "OrderAgent size yardımcı olacak" gibi bir cümleyi duyuyordu** — yani `RewriteRoutingMessageAsync` savunması yalnızca yazılı sohbet ekranı için çalışıyor, kalıcılığı ve sesi baypas ediyordu. Artık tüketiciler bu metni kanonik kaynak olarak kullanır; event hiç gelmezse (hata/iptal) delta birleşimine geri düşülür.

## 6. Tipler ve Üyeler

### `StreamEvent(string Type, object? Data)`
Ana zarf — `Type` event adı (`StreamEventTypes` sabitlerinden biri), `Data` JSON'a serileştirilebilir herhangi bir nesne.

### `SessionEventPayload(string SessionId)`
`"session"` event'inin payload'ı — `SessionId` alanının adının sabit/typed olmasını garanti eder (rename'de sessiz hata riskini önler).

### `TextDeltaPayload(string Text)`
`response_delta`/`reasoning_delta` event'lerinin payload'ı.

### `ResponseCompletePayload(string Text, string? TerminationReason, bool? Revised, bool? Decomposed, int? SubTaskCount)`
`response_complete` event'inin payload'ı — turun **kanonik** (delta'lardan farklı, temizlenmiş) nihai metnini taşır. `[JsonIgnore(WhenWritingNull)]` ile null alanlar JSON çıktısından düşürülür (gereksiz gürültü olmasın diye).

### `StreamEventTypes` — Bilinen Event Tipleri

| Sabit | Değer | Ne zaman yayınlanır |
|---|---|---|
| `Session` | `"session"` | Oturum kimliği belirlendiğinde. |
| `ReasoningStart`/`ReasoningDelta`/`ReasoningComplete` | — | Reasoning aşamasının başlangıcı/artımı/bitişi. |
| `Agent` | `"agent"` | Hangi uzman ajanın devrede olduğu bildirilir. |
| `ResponseStart`/`ResponseDelta`/`ResponseComplete` | — | Yanıt üretiminin başlangıcı/artımı/bitişi. |
| `Error`/`Done` | — | Hata/turun bittiği sinyali. |
| `ApprovalRequired` | `"approval_required"` | Bir tool çağrısı admin onayı bekliyor; payload `ApprovalRequest`. |
| `ApprovalResolved` | `"approval_resolved"` | Onay kararı verildi; payload `{ id, status, reason? }`. |
| `EscalationCreated` | `"escalation_created"` | Yeni eskalasyon kaydı oluştu; payload `EscalationRequest`. |
| `HumanJoined`/`HumanMessage`/`HumanLeft` | — | Canlı devralma (takeover) yaşam döngüsü. |
| `HandoffPending`/`HandoffCleared` | — | Kullanıcıya "temsilci bağlanıyor" bildirimi ve iptali. |
| `BridgeMessage` | `"bridge_message"` | Admin SSE — bridge üzerinden user mesajı admin paneline iletilir. |
| `BotTyping` | `"bot_typing"` | Bot arka planda otomatik yanıt hazırlıyor (ör. admin replan sonrası); payload `{ on: bool }`. |
| `SentimentUpdate`/`SentimentAlert` | — | Tur bazlı duygu güncellemesi / kritik eşik altına düşme. |
| `UiHint` | `"ui_hint"` | Tool çıktısına bağlı frontend UI bileşeni sinyali (ör. `kind="category_picker"`). |

## 7. Bağımlılıklar

`System.Text.Json.Serialization` (`JsonIgnore`).

## Bağlantılar

- [IChatPort](IChatPort.md), [IHitlEventPort](IHitlEventPort.md)

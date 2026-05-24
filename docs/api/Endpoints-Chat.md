# Endpoints — Chat & Realtime & Session

**Dosyalar:**
- `Endpoints/ChatEndpoints.cs` — Text chat (anonymous)
- `Endpoints/RealtimeEndpoints.cs` — Voice (WebSocket)
- `Endpoints/SessionEndpoints.cs` — Session history (debug)

---

## ChatEndpoints

Public chat — JWT yok, sadece IP-based rate limit.

| Route | Method | Auth | Rate limit | Açıklama |
|---|---|---|---|---|
| `/chat/` | POST | Anonymous | `chat` (20/dak) | Non-streaming chat |
| `/chat/stream` | POST | Anonymous | `chat` (20/dak) | SSE stream (reasoning + delta + HITL) |
| `/chat/events/{sessionId}` | GET | Anonymous | — | Persistent SSE (oturum boyu) |

---

### `POST /chat/`

```http
POST /chat/
Content-Type: application/json

{ "sessionId": "sess-123", "message": "ORD-5 nerede" }
```

**Akış:**

```
1. IInputGuard.Inspect(message)
   - 7 kural (length, injection, ID enumeration, vb.)
   - Reddet → 400 Bad Request
2. IChatPort.HandleAsync(sessionId, message)
   - WorkflowExecutor dene (regex match)
   - Eşleşme yoksa → Reasoning + AgentTeam pipeline
3. Response döner
```

**200 Response:**

```json
{
  "sessionId": "sess-123",
  "reply": "Sipariş ORD-5 'Kargoda' durumunda...",
  "traceId": "trace-abc"
}
```

**400 Response (InputGuard reject):**

```json
{
  "status": 400,
  "title": "Input rejected",
  "detail": "Mesaj çok kısa veya geçersiz karakterler içeriyor"
}
```

---

### `POST /chat/stream`

SSE — yanıt parça parça akıyor. Reasoning step'leri, workflow event'leri, HITL durumları da yayılır.

```http
POST /chat/stream
Content-Type: application/json
Accept: text/event-stream

{ "sessionId": "sess-123", "message": "ORD-5 nerede" }
```

**SSE event sırası (örnek):**

```
event: session
data: { "sessionId": "sess-123" }

event: traceStarted
data: { "traceId": "trace-abc", "startedAt": "..." }

event: reasoning
data: { "intent": "OrderInquiry", "confidence": 0.95, "agent": "OrderAgent" }

event: agentStarted
data: { "name": "OrderAgent" }

event: toolCall
data: { "name": "order_status_tool", "args": { "order_id": "ORD-5" } }

event: toolResult
data: { "success": true, "data": { "status": "Kargoda" } }

event: chunk
data: { "text": "Sipariş ORD-5 " }

event: chunk
data: { "text": "kargoda durumda..." }

event: done
data: { "sessionId": "sess-123" }
```

### HITL bridging

Stream sırasında approval gerekirse:

```
event: approvalRequested
data: { "approvalId": "app-xxx", "toolName": "order_placement_tool", "params": {...} }

(beklenir, admin onay verir)

event: approvalDecided
data: { "approvalId": "app-xxx", "status": "Approved" }

event: chunk
data: { "text": "Siparişiniz oluşturuldu..." }
```

Veya escalation:

```
event: escalationCreated
data: { "escalationId": "esc-yyy", "reason": "Bot çözemiyor" }

event: humanJoined
data: { "humanAgent": "Ali", "enteredAt": "..." }
```

### Disconnect handling

Client kapatırsa `HttpContext.RequestAborted` cancel olur — endpoint cleanup yapar:
- LLM çağrısı iptal edilir (downstream cancellation)
- DB write'lar tamamlanır
- Orphan escalation dismiss edilir

---

### `GET /chat/events/{sessionId}`

**Persistent SSE** — bir kez bağlan, oturum boyu açık kal. ChatEventOrchestrator orkestre eder.

```http
GET /chat/events/sess-123?access_token=eyJ...
Accept: text/event-stream
```

JWT query string ile (EventSource header gönderemez).

**Event tipleri:** `session`, `humanJoined`, `humanLeft`, `handoffPending`, `handoffCleared`, `botTyping`, `humanMessage`, `done`.

Detay: [Services.md](Services.md) (ChatEventOrchestrator).

---

## RealtimeEndpoints

WebSocket — sesli sohbet.

| Route | Method | Auth | Mod |
|---|---|---|---|
| `/chat/realtime/{sessionId?}` | WS | Anonymous | Bridge — agent pipeline + TTS |
| `/chat/realtime-native/{sessionId?}` | WS | Anonymous | Native — OpenAI direkt yanıt |

### Bağlantı

```javascript
const ws = new WebSocket('wss://api.example.com/chat/realtime/sess-123');
```

Server WS upgrade'i kabul eder, `WebSocketBrowserChannel` oluşturur, ilgili bridge servisine teslim eder.

### Bridge mode (`/chat/realtime/`)

```
Browser ──audio─→ Api ──→ RealtimeBridgeService (Application)
                              ↓
                        OpenAI Realtime (STT only)
                              ↓ transcript
                        Reasoning + AgentTeam pipeline
                              ↓ response text
                        OpenAI Realtime (TTS)
                              ↓ audio
                        Browser ←─audio──
```

Avantaj: Full HITL pipeline (approval, escalation çalışır).  
Dezavantaj: Latency yüksek (~1-2 saniye round-trip).

### Native mode (`/chat/realtime-native/`)

```
Browser ──audio─→ Api ──→ RealtimeNativeService
                              ↓
                        OpenAI Realtime (STT + LLM + TTS, tool dispatch)
                              ↓ audio
                        Browser ←─audio──
```

Avantaj: Düşük latency (~500ms).  
Dezavantaj: Sadece read-only tool'lar (sipariş oluşturma, şikayet → text chat'e yönlendir).

### SessionId opsiyonel

`sessionId` URL'de yoksa server yeni session ID oluşturur. Browser ilk binding event'inden öğrenir:

```json
{ "type": "session.created", "sessionId": "sess-abc" }
```

### Disconnect

Browser veya server kapatırsa:
- `WebSocketBrowserChannel.IsOpen = false`
- `IRealtimeVoiceTransport.CloseAsync` (OpenAI WS kapat)
- Bridge servisi cleanup yapar

---

## SessionEndpoints

Sidebar/debug — session geçmişi ve state göster.

| Route | Method | Auth | Açıklama |
|---|---|---|---|
| `/sessions/` | GET | Anonymous | Tüm session'lar (metadata) |
| `/sessions/{sessionId}/messages` | GET | Anonymous | Mesaj history |
| `/sessions/{sessionId}/state` | GET | Anonymous | Session state (intent, phase, sentiment) |

---

### `GET /sessions/`

```http
GET /sessions/
```

**200 Response:**

```json
[
  {
    "sessionId": "sess-123",
    "createdAt": "2026-05-24T10:00:00Z",
    "lastActivity": "2026-05-24T10:15:00Z",
    "messageCount": 12
  },
  // ...
]
```

Sidebar UI için — son aktivite sırasına göre liste.

---

### `GET /sessions/{sessionId}/messages`

```http
GET /sessions/sess-123/messages
```

**200 Response:**

```json
[
  { "role": "user", "text": "Merhaba" },
  { "role": "bot", "text": "Merhaba, size nasıl yardımcı olabilirim?" },
  { "role": "user", "text": "ORD-5 nerede" },
  // ...
]
```

`ConversationMessage.Role` mapping:
- `user` → `"user"`
- `assistant` → `"bot"`
- `system` → atlanır (kullanıcıya gösterilmez)

---

### `GET /sessions/{sessionId}/state`

```http
GET /sessions/sess-123/state
```

**200 Response:**

```json
{
  "sessionId": "sess-123",
  "createdAt": "2026-05-24T10:00:00Z",
  "lastActivity": "2026-05-24T10:15:00Z",
  "state": {
    "customerId": "CUST-1990",
    "currentIntent": "OrderInquiry",
    "phase": "Action",
    "turnCount": 5,
    "sentiment": "neutral",
    "sentimentScore": 0.5,
    "consecutiveNegativeTurns": 0,
    "collectedInfo": {
      "order_id": "ORD-5",
      "customer_id": "CUST-1990"
    }
  }
}
```

Debug için faydalı — bot'un session hakkında ne bildiğini görmek.

---

## Anonymous neden?

Public chat — kayıt olmadan kullanılabilir. SessionId browser tarafında üretilir (LocalStorage). Auth gerek yok çünkü:

- Sıfır friction (login zorunluluğu UX'i bozar)
- Session ID public/random — başkasının session'ına erişmek zor (32+ karakter)
- Rate limit IP başına — abuse engellenir

**Sınırlar:**
- Aynı browser farklı session açabilir (LocalStorage temizlenirse kayıp)
- Cross-device sync yok
- Admin paneli session ID gözlemleyebilir ama içeriği değişiklik için JWT lazım

---

## Bağlantılar

- [Application ChatPort](../application/README.md)
- [Application InputGuard](../application/InputGuard.md)
- [Application RealtimeServices](../application/RealtimeServices.md)
- [Adapters.AI Realtime](../adapters-ai/Realtime.md)
- [Services.md](Services.md) — ChatEventOrchestrator
- [Infrastructure.md](Infrastructure.md) — SseWriter, WebSocketBrowserChannel

# Endpoints — Chat & Realtime & Session

**Dosyalar:**
- `Endpoints/ChatEndpoints.cs` — Text chat (rate-limited)
- `Endpoints/RealtimeEndpoints.cs` — Voice (WebSocket)
- `Endpoints/SessionEndpoints.cs` — Session history (sidebar/debug)

---

## ChatEndpoints

| Route | Method | Auth | Rate limit | Açıklama |
|---|---|---|---|---|
| `/chat/` | POST | **`Customer`** | `chat` (20/dak) | Non-streaming chat |
| `/chat/stream` | POST | **`Customer`** | `chat` (20/dak) | SSE stream (reasoning + delta + HITL) |
| `/chat/events/{sessionId}` | GET | **`Customer`** | — | Persistent SSE (oturum boyu) |
| `/chat-sessions/{sessionId}/approvals/unseen` | GET | **`Customer`** | — | Kaçırılan onay sonuçları (badge) |
| `/chat-sessions/{sessionId}/approvals/{id}/seen` | POST | **`Customer`** | — | Bildirimi "görüldü" işaretle |
| `/customer/approvals/history` | GET | **`Customer`** | — | Müşterinin tüm onay geçmişi |

> Chat anonim **değildir**. Müşteri e-posta+şifre ile login olur; `customerId` JWT claim'inden
> okunur, kullanıcının serbest metninden çıkarılmaz. Bu olmadan biri başkasının müşteri
> numarasını söyleyip onun adına sipariş iptali/iade başlatabilirdi.
>
> `/customer/approvals/history` müşteri kimliğini **route'tan değil** JWT'den alır — URL
> değiştirerek başkasının geçmişini görmek mümkün değildir.
>
> Onay uçları oturum sahipliğine ek olarak **kaydın müşterisini** de doğrular. Sahiplik tek
> başına yetmez: onay kayıtları oturumdan bağımsız yaşar (`session_id` için yabancı anahtar
> yoktur) ve sahiplik kontrolü var olmayan oturumlara izin verir — ilk temas oturumu çağırana
> bağlasın diye. İkisi birleşince, silinmiş bir oturumun id'sini öğrenen başka bir müşteri
> bildirimleri okuyabilirdi.

---

### `POST /chat/`

```http
POST /chat/
Content-Type: application/json

{ "sessionId": "sess-123", "query": "5 nerede" }
```

**Akış:**

```
1. IInputGuard.Inspect(query)
   - Kural setleri (length, injection, ID enumeration, vb.)
   - Verdict=Reject → 400 Bad Request (error, message, flags)
   - Flags varsa → LogWarning + devam
2. IChatPort.HandleAsync(safeRequest)
   - Reasoning + AgentTeam pipeline
3. Response döner
```

**400 Response (InputGuard reject):**

```json
{
  "error": "input_blocked",
  "message": "Mesaj kabul edilemiyor.",
  "flags": ["injection_pattern"]
}
```

---

### `POST /chat/stream`

SSE — yanıt parça parça akıyor. Reasoning step'leri, workflow event'leri, HITL durumları da yayılır.

```http
POST /chat/stream
Content-Type: application/json
Accept: text/event-stream

{ "sessionId": "sess-123", "query": "5 nerede" }
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
data: { "name": "order_status_tool", "args": { "order_id": "5" } }

event: toolResult
data: { "success": true, "data": { "status": "Kargoda" } }

event: chunk
data: { "text": "Sipariş 5 kargoda durumda..." }

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

HITL subscription, session event'inden session ID alındıktan sonra `IHitlEventPort.Subscribe(resolvedSessionId, callback)` ile kurulur. Akış bitince subscription dispose edilir.

InputGuard reject ederse:

```
event: session
data: { "sessionId": "unknown" }

event: response_complete
data: { "content": "Mesaj kabul edilemedi.", "blocked": true, "flags": ["..."] }
```

### Disconnect handling

Client kapatırsa `HttpContext.RequestAborted` cancel olur — endpoint cleanup yapar:
- HITL subscription dispose edilir
- `sse.WriteDoneAsync(resolvedSessionId)` çağrılır

---

### `GET /chat/events/{sessionId}`

**Persistent SSE** — bir kez bağlan, oturum boyu açık kal. `ChatEventOrchestrator` orkestre eder.

```http
GET /chat/events/sess-123?access_token=eyJ...
Accept: text/event-stream
```

JWT query string ile (EventSource header gönderemez).

**Event tipleri:** `session`, `human_joined`, `handoff_pending`, `bot_typing`, `human_message`, `done`.

Detay: [Services.md](Services.md) (ChatEventOrchestrator).

---

## RealtimeEndpoints

WebSocket — sesli sohbet.

| Route | Method | Auth | Mod |
|---|---|---|---|
| `/chat/realtime/{sessionId?}` | WS | **`Customer`** | Bridge — agent pipeline + TTS |
| `/chat/realtime-native/{sessionId?}` | WS | **`Customer`** | Native — OpenAI direkt yanıt |

WebSocket olmayan isteğe 400 döner. Her iki route da `WebSocketBrowserChannel` oluşturur ve ilgili bridge servisine delege eder.

### Bağlantı

```javascript
const ws = new WebSocket('wss://api.example.com/chat/realtime/sess-123');
```

### Bridge mode (`/chat/realtime/`)

```
Browser ──audio─→ Api ──→ IRealtimeBridge.RunAsync (Application)
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
Browser ──audio─→ Api ──→ IRealtimeNativeBridge.RunAsync
                              ↓
                        OpenAI Realtime (STT + LLM + TTS, tool dispatch)
                              ↓ audio
                        Browser ←─audio──
```

Avantaj: Düşük latency (~500ms).
Dezavantaj: Sadece read-only tool'lar (sipariş oluşturma, şikayet → text chat'e yönlendir).

### SessionId opsiyonel

`sessionId` URL'de yoksa server `Guid.NewGuid().ToString()` ile oluşturur.

### Disconnect

Browser veya server kapatırsa `CloseGracefullyAsync` çalışır — `WebSocketCloseStatus.NormalClosure` gönderilir.

---

## SessionEndpoints

Sidebar/debug — session geçmişi ve state göster.

| Route | Method | Auth | Açıklama |
|---|---|---|---|
| `/sessions/` | GET | **`SessionAccess`** | Çağıranın oturumları (admin/agent: hepsi) |
| `/sessions/{sessionId}/messages` | GET | **`SessionAccess`** + sahiplik | Mesaj history |
| `/sessions/{sessionId}/state` | GET | **`SessionAccess`** + sahiplik | Session state (createdAt, lastActivity, state) |

> Bu üç uç eskiden **anonimdi**: session id'sini bilen biri konuşmaları okuyabiliyor, `/sessions/`
> ise tüm oturumları listeleyerek id'leri de veriyordu — yani `/chat/*` üzerindeki müşteri
> yetkilendirmesi bu yoldan tamamen dolaşılabiliyordu.
>
> İki katman birden uygulanır:
> 1. **Kimlik** — `SessionAccess` politikası: yalnızca `Customer`, `Admin`, `Agent`. Çıplak
>    `RequireAuthorization()` yetmez: A2A token'ları da aynı JWT şemasıyla doğrulanır, yani
>    "kimliği doğrulanmış olmak" ölçüt yapılırsa partner token'ı da girer. Partner token'ında
>    `linked_customer_id` yoktur; kapsam "claim yoksa sınırsız" diye hesaplanırsa partner
>    **admin gibi** değerlendirilip tüm müşterilerin oturumlarını okur. Sınırsız erişim bu
>    yüzden claim'in yokluğundan değil **rolden** türetilir.
> 2. **Sahiplik** — "bu kişi müşteri mi" ile "bu oturum onun mu" farklı sorulardır. Kontrol
>    `SessionIdentityBinder` üzerinden yapılır, yani `/chat/*` ile **aynı** kuraldır.
>
> Liste ucunda sahipliğin karşılığı kapsamdır: müşteri yalnızca kendi oturumlarını görür.
> Filtre uç noktada değil veri kaynağında (`ISessionManager.GetAllSessionsAsync(forCustomerId)`)
> uygulanır. Admin/agent'ın `linked_customer_id` claim'i yoktur, bu yüzden kapsam sınırlaması
> onlara uygulanmaz — panelin tüm oturumları görmesi bilinçlidir.

---

### `GET /sessions/`

```http
GET /sessions/
```

`ISessionPort.GetAllSessions()` döner.

---

### `GET /sessions/{sessionId}/messages`

```http
GET /sessions/sess-123/messages
```

**200 Response:**

```json
[
  { "role": "user", "text": "Merhaba" },
  { "role": "bot", "text": "Merhaba, size nasıl yardımcı olabilirim?" }
]
```

`ConversationMessage.Role` mapping:
- `User` → `"user"`
- diğer → `"bot"`

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
  "state": { ... }
}
```

Session bulunamazsa 404 döner.

---

## Bağlantılar

- [Application ChatPort](../CustomerSupportBot.Application/README.md)
- [Application InputGuard](../CustomerSupportBot.Application/Chat/InputGuard.md)
- [Application RealtimeServices](../CustomerSupportBot.Application/Realtime/RealtimeBridgeService.md)
- [Adapters.AI Realtime](../CustomerSupportBot.Adapters.AI/Realtime.md)
- [Services.md](Services.md) — ChatEventOrchestrator
- [Infrastructure.md](Infrastructure.md) — SseWriter, WebSocketBrowserChannel

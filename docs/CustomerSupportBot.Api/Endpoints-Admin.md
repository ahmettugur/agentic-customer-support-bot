# Endpoints — Admin & Agent Panel & Agents

**Dosyalar:**
- `Endpoints/AdminEndpoints.cs` — `/approvals`, `/escalations`, `/chat-sessions` (Admin scope)
- `Endpoints/AgentPanelEndpoints.cs` — `/agent/*` (AdminOrAgent scope, paralel UI)
- `Endpoints/AgentsEndpoints.cs` — `/agents` registry CRUD

HITL operasyonlarının HTTP yüzeyi — onay, escalation, takeover, agent yönetimi.

---

## AdminEndpoints — `/approvals/*`

Pending approval queue yönetimi.

| Route | Method | Açıklama |
|---|---|---|
| `/approvals/pending` | GET | Bekleyen approval'lar |
| `/approvals/recent?count=50` | GET | Son N karar |
| `/approvals/stuck` | GET | Onaylanmış ama yürütmesi askıda kalmış kayıtlar (tarih sınırı yok) |
| `/approvals/{id}` | GET | Tek approval |
| `/approvals/{id}/approve` | POST | Onayla (body: `{ decidedBy?, reason? }`) |
| `/approvals/{id}/reject` | POST | Reddet (body: `{ decidedBy?, reason? }`) |

### High-risk tool kuralı

`WellKnown.HighRiskTools` listesindeki tool'lar için **reason zorunlu**:

```csharp
if (WellKnown.HighRiskTools.Contains(req.ToolName) &&
    string.IsNullOrWhiteSpace(body?.Reason))
{
    return Results.BadRequest(new
    {
        error = "approval_reason_required",
        message = $"'{req.ToolName}' yüksek riskli bir işlem; onay için gerekçe zorunludur."
    });
}
```

Audit trail için kritik — yüksek riskli işlem neden onaylandı?

### Pending listesi

```http
GET /approvals/pending
Authorization: Bearer <admin-jwt>
```

```json
[
  {
    "id": "app-abc",
    "sessionId": "sess-123",
    "toolName": "order_placement_tool",
    "agentName": "OrderAgent",
    "parameters": { "product": "iPhone 15", "quantity": 1 },
    "userQuery": "iPhone 15 alabilir miyim?",
    "requestedAt": "2026-05-24T10:00:00Z"
  }
]
```

---

## AdminEndpoints — `/escalations/*`

Open escalation queue yönetimi.

| Route | Method | Açıklama |
|---|---|---|
| `/escalations/open` | GET | Açık escalation'lar |
| `/escalations/recent?count=50` | GET | Son N escalation |
| `/escalations/{id}` | GET | Tek escalation |
| `/escalations/{id}/acknowledge` | POST | Agent'a ata, sistemden müşteriye bildir |
| `/escalations/{id}/resolve` | POST | Çözüldü olarak işaretle |
| `/escalations/{id}/dismiss` | POST | False positive, kapat |
| `/escalations/{id}/replan` | POST | Çöz + Release + replan tetikle |

### `acknowledge` davranışı

```csharp
escalations.Decide(id, WellKnown.EscalationActions.Acknowledge, assignedTo: body?.AssignedTo);
chatSessions.PublishSystemMessage(esc.SessionId,
    $"ℹ️ {agentLabel} talebinizi üstlendi ve sizinle daha sonra iletişime geçecek.");
```

Müşteriye sistem mesajı düşülür — "biri size geliyor" bilgisi.

### `replan` davranışı

```csharp
app.MapPost("/escalations/{id}/replan", (string id, ReplanInput? body, IChatSessionPort chatSessions) =>
{
    var result = chatSessions.ReplanEscalation(id, requestedBy, note);
    // 1. Escalation'ı resolve et
    // 2. ChatMode'u Bot'a döndür (Release)
    // 3. Replan use-case'ini kuyruğa al
    return Results.Json(new { id, sessionId, status = "replan_queued", requestedBy, releasedFromHuman });
});
```

"Bot tekrar denesin" senaryosu — admin manuel tetikler.

---

## AdminEndpoints — `/chat-sessions/*`

Aktif session'lar üzerinde admin operasyonları.

| Route | Method | Açıklama |
|---|---|---|
| `/chat-sessions/active` | GET | Human modundaki session'lar |
| `/chat-sessions/{sid}/state` | GET | Mode + human agent info |
| `/chat-sessions/{sid}/history?take=50` | GET | Mesaj geçmişi |
| `/chat-sessions/{sid}/sentiment` | GET | Sentiment trend |
| `/chat-sessions/{sid}/takeover` | POST | Bot → Human modu |
| `/chat-sessions/{sid}/release` | POST | Human → Bot modu |
| `/chat-sessions/{sid}/messages` | POST | Admin mesajı yolla |
| `/chat-sessions/{sid}/subscribe` | GET | SSE — kullanıcı mesajları akışı |
| `/chat-sessions/{sid}/replan` | POST | Escalation'sız replan |

### `takeover`

```http
POST /chat-sessions/sess-123/takeover
Authorization: Bearer <admin-jwt>
Content-Type: application/json

{ "humanAgent": "Ali Demir" }
```

`IChatSessionPort.TakeOver(sid, agent, agent)` çağrılır. Başarılı döner:

```json
{
  "sessionId": "sess-123",
  "mode": "human",
  "humanAgent": "Ali Demir",
  "escalationsAcknowledged": 1
}
```

### `subscribe` — admin live view

Admin paneli session'ı dinler — müşteri mesajları SSE ile akar.

```http
GET /chat-sessions/sess-123/subscribe?access_token=...
Accept: text/event-stream
```

Server `IChatSessionPort.SubscribeToAdminAsync(sessionId)` üzerinden mesaj akıtır.

```
event: session
data: { "sessionId": "sess-123" }

event: bridge_message
data: { "id": "msg-1", "sessionId": "...", "sender": "user", "text": "...", "timestamp": "..." }
```

Admin canlı izler — gerekirse `takeover` yapar.

### `sentiment`

```http
GET /chat-sessions/sess-123/sentiment
```

```json
{
  "sentiment": "negative",
  "score": 0.25,
  "consecutiveNegative": 2,
  "history": [
    { "turn": 1, "label": "neutral", "score": 0.5 },
    { "turn": 2, "label": "negative", "score": 0.3 }
  ]
}
```

Negatif trend → admin proaktif takeover yapabilir.

---

## AgentPanelEndpoints — `/agent/*`

Insan agent (Role=Agent veya Admin) paneli. Admin'in subset'i + agent-spesifik filtreleme. `AdminOrAgent` policy ile korunur.

### JWT claim okuması

```csharp
private static string? GetLinkedAgentId(HttpContext ctx) =>
    ctx.User.FindFirstValue("linked_agent_id");
```

`linked_agent_id` JWT claim'i — UserInfo.LinkedAgentId'den gelir.

### Escalation endpoint'leri

| Route | Method | Açıklama |
|---|---|---|
| `/agent/escalations/my` | GET | `linked_agent_id`'ye atanmış eskalasyonlar |
| `/agent/escalations/open` | GET | Boş veya bu agent'a atananlar |
| `/agent/escalations/recent?count=50` | GET | Geçmiş — **aynı kapsam**: boş veya bu agent'a atananlar |
| `/agent/escalations/{id}/acknowledge` | POST | Üstlen + müşteriye bildir |
| `/agent/escalations/{id}/resolve` | POST | Çöz + `DecrementLoad` |
| `/agent/escalations/{id}/dismiss` | POST | Reddet |
| `/agent/escalations/{id}/replan` | POST | Replan tetikle |

**Kapsam kuralı:** Sınırsız erişim **`Admin` rolünden** türetilir, `linked_agent_id` claim'inin
yokluğundan DEĞİL. `LinkedAgentId` veritabanında nullable'dır; "claim yoksa hepsini göster"
kuralı, bağlantısı kurulmamış bir `Agent` hesabını sessizce Admin kapsamına yükseltirdi —
üstelik eylem uçları claim yoksa zaten 400 döndüğü için liste uçları onlardan daha geniş olurdu.
Bağlantısız bir Agent liste uçlarında da **400** alır.

Kapsam daraltması ve limit **birlikte, veritabanında** uygulanır (`IEscalationSink.GetRecentForAgentAsync`).
İki tuzak birden vardır ve ikisi de agent'ın kendi kayıtlarını görememesine yol açar:

1. `count` ile kesip sonra elemek — o N kaydın tamamı başkalarına aitse liste boş döner.
2. Cache üzerinde filtrelemek — `PostgresEscalationSink` cache'i açık kayıtlar + son **500**
   kapalı kayıttır, yani cache'in kendisi bir "son N" penceresidir. Orada elemek sınırı 50'den
   500'e ötelemekten ibarettir, kaldırmaz.

### Approval endpoint'leri

| Route | Method | Açıklama |
|---|---|---|
| `/agent/approvals/pending` | GET | Tüm pending |
| `/agent/approvals/recent?count=50` | GET | Son N karar (geçmiş sekmesi) |
| `/agent/approvals/stuck` | GET | Yürütmesi askıda kalmış onaylar (tarih sınırı yok) |
| `/agent/approvals/{id}/approve` | POST | Onayla (high-risk için reason zorunlu) |
| `/agent/approvals/{id}/reject` | POST | Reddet |

### Chat session endpoint'leri (Live Takeover)

| Route | Method | Açıklama |
|---|---|---|
| `/agent/chat-sessions/active` | GET | Human modda session'lar |
| `/agent/chat-sessions/{sid}/takeover` | POST | Sohbete katıl |
| `/agent/chat-sessions/{sid}/release` | POST | Bırak (Bot moda dön) |
| `/agent/chat-sessions/{sid}/messages` | POST | Müşteriye mesaj |
| `/agent/chat-sessions/{sid}/history` | GET | Geçmiş |
| `/agent/chat-sessions/{sid}/sentiment` | GET | Sentiment (read-only) |
| `/agent/chat-sessions/{sid}/subscribe` | GET | SSE — müşteri mesajlarını dinle |
| `/agent/chat-sessions/{sid}/replan` | POST | Replan tetikle |

### `/agent/profile`

```http
GET /agent/profile
Authorization: Bearer <agent-jwt>
```

```json
{
  "id": "agent-1",
  "displayName": "Ali Demir",
  "skills": ["complaint", "tr", "vip"],
  "languages": ["tr"],
  "maxConcurrentLoad": 5,
  "priority": 1
}
```

Agent kendi profil bilgilerini görür. `linked_agent_id` claim yoksa 400 döner.

### Endpoint farkları (admin vs agent)

| Aksiyon | Admin (`/`) | Agent (`/agent/`) |
|---|---|---|
| Tüm pending'leri görme | ✅ | ✅ (atanmamış + kendine ait) |
| Escalation acknowledge | Herhangi birini | Herhangi birini (load increment yapılır) |
| Approve/Reject | ✅ | ✅ |
| Agent registry CRUD | ✅ | ❌ |
| Profil görme | Herhangi biri | Sadece kendi |

### Load tracking

```
Acknowledge → IHumanAgentPort.IncrementLoad
Resolve     → IHumanAgentPort.DecrementLoad
TakeOver    → IncrementLoad (zımni)
Release     → DecrementLoad (zımni)
```

`CurrentLoad` agent'ın eş zamanlı konuşma sayısı — `MaxConcurrentLoad`'a yaklaşınca SkillsBasedRouter agent'a düşük score verir.

---

## AgentsEndpoints — `/agents` CRUD

| Route | Method | Açıklama |
|---|---|---|
| `/agents` | GET | Tüm agent'lar (registry + auth-linked merged) |
| `/agents` | POST | Yeni agent oluştur |
| `/agents/{id}` | GET | Tek agent |
| `/agents/{id}` | PUT | Güncelle |
| `/agents/{id}` | DELETE | Sil |
| `/escalations/{id}/reroute` | POST | Manuel reroute |

### POST request örneği

```http
POST /agents
Authorization: Bearer <admin-jwt>
Content-Type: application/json

{
  "displayName": "Ali Demir",
  "email": "ali@example.com",
  "isActive": true,
  "skills": ["complaint", "tr", "vip"],
  "languages": ["tr", "en"],
  "maxConcurrentLoad": 5,
  "priority": 1
}
```

### Reroute

```http
POST /escalations/esc-xxx/reroute
Authorization: Bearer <admin-jwt>
Content-Type: application/json

{ "agentId": "agent-2", "reason": "İlk agent meşgul, VIP müşteri" }
```

Skills router otomatik seçim yaptı ama admin manuel override edebilir.

### Merge logic

`GET /agents` endpoint **iki kaynak**'tan agent'ları birleştirir:

1. `IHumanAgentPort.GetAllMergedAsync(ct)` — registry + auth-linked user merge

Bazı agent'lar registry'de var ama user yok. Bazıları user var ama LinkedAgentId set edilmemiş. `GetAllMergedAsync` ikisini birleştirir; response `{ id, displayName, isActive }` içerir.

---

## Bağlantılar

- [Application ApprovalPortService](../CustomerSupportBot.Application/Approval/ApprovalPortService.md)
- [Application EscalationPortService](../CustomerSupportBot.Application/Escalation/EscalationPortService.md)
- [Application ChatSessionPortService](../CustomerSupportBot.Application/Chat/ChatSessionPortService.md)
- [Application HumanAgentPortService](../CustomerSupportBot.Application/Escalation/HumanAgentPortService.md)
- [Domain Model-Hitl](../CustomerSupportBot.Domain/Model/ApprovalRequest.md)
- [Models.md](Models.md) — AdminModels DTO'lar

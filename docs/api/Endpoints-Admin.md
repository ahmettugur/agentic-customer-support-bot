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
| `/approvals/{id}` | GET | Tek approval |
| `/approvals/{id}/approve` | POST | Onayla |
| `/approvals/{id}/reject` | POST | Reddet |

### High-risk tool kuralı

`order_placement_tool` ve `complaint_registration_tool` için **reason zorunlu**:

```csharp
adminGroup.MapPost("/approvals/{id}/approve", async (string id, ApprovalDecisionInput input) =>
{
    var approval = await _approval.GetAsync(id);
    if (_approvalOptions.ToolsRequiringApproval.Contains(approval.ToolName))
    {
        if (string.IsNullOrWhiteSpace(input.Reason))
            return Results.BadRequest("High-risk tool için reason zorunlu");
    }
    await _approval.DecideAsync(id, approved: true, decidedBy, input.Reason);
    return Results.NoContent();
});
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
    "createdAt": "2026-05-24T10:00:00Z",
    "timeoutSeconds": 300
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
adminGroup.MapPost("/escalations/{id}/acknowledge", async (string id, AckInput input) =>
{
    await _escalation.AcknowledgeAsync(id, agentId: input.AgentId);
    await _chatSession.PublishSystemMessage(esc.SessionId,
        $"{input.HumanAgent} adlı temsilci sohbetinize bağlanıyor...");
});
```

Müşteriye sistem mesajı düşülür — "biri size geliyor" bilgisi.

### `replan` davranışı

```csharp
adminGroup.MapPost("/escalations/{id}/replan", async (string id, ReplanInput input) =>
{
    await _escalation.ReplanAsync(id);
    // 1. Escalation'ı resolve et
    // 2. ChatMode'u Bot'a döndür (Release)
    // 3. ForceReplanNextTurn = true (session.state)
    // 4. ReplanService.ExecuteAsync arka planda tetikle
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

**Akış:**

```
IChatSessionPort.TakeOverAsync(sessionId, agentId, humanAgent)
   ├── IChatModeRegistry.SetMode(sessionId, ChatMode.Human, agentId)
   ├── IEscalationSink.AcknowledgeEscalationsForSession(sessionId)
   ├── IHumanAgentRegistry.IncrementLoad(agentId)
   └── IChatBridge.PublishSystemMessage(...)
```

### `subscribe` — admin live view

Admin paneli session'ı dinler — müşteri mesajları SSE ile akar.

```http
GET /chat-sessions/sess-123/subscribe?access_token=...
Accept: text/event-stream
```

Server `IChatBridge.SubscribeToAdminAsync(sessionId)` üzerinden mesaj akıtır.

```
event: userMessage
data: { "id": "msg-1", "text": "Sorun çözülmedi", "at": "..." }

event: botMessage
data: { "id": "msg-2", "text": "Anlıyorum, alternatifler...", "at": "..." }
```

Admin canlı izler — gerekirse `takeover` yapar.

### `sentiment`

```http
GET /chat-sessions/sess-123/sentiment
```

```json
{
  "sentiment": "negative",
  "sentimentScore": 0.25,
  "consecutiveNegativeTurns": 2,
  "history": [
    { "turn": 1, "label": "neutral", "score": 0.5 },
    { "turn": 2, "label": "negative", "score": 0.3 },
    { "turn": 3, "label": "negative", "score": 0.25 }
  ]
}
```

Negatif trend → admin proaktif takeover yapabilir.

---

## AgentPanelEndpoints — `/agent/*`

Insan agent (Role=Agent) paneli. Admin'in subset'i + agent-spesifik filtreleme.

### JWT claim okuması

```csharp
agentGroup.MapGet("/escalations/my", async (HttpContext ctx) =>
{
    var linkedAgentId = ctx.User.FindFirst("linked_agent_id")?.Value;
    if (string.IsNullOrEmpty(linkedAgentId))
        return Results.Forbid();   // Bağlı agent kaydı yoksa erişim yok
    return await _escalation.GetAssignedToAsync(linkedAgentId);
});
```

`linked_agent_id` JWT claim'i — UserInfo.LinkedAgentId'den gelir.

### Endpoint farkları (admin vs agent)

| Aksiyon | Admin | Agent |
|---|---|---|
| Tüm pending'leri görme | ✅ | Sadece kendine atananlar + uygun olanlar |
| Acknowledge | Herhangi birini | Sadece kendine veya boş olanları |
| Approve/Reject | ✅ | ✅ |
| Agent registry CRUD | ✅ | ❌ |

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
  "currentLoad": 2,
  "priority": 1
}
```

Agent kendi profile bilgilerini görür.

### Load tracking

```
Acknowledge → IHumanAgentRegistry.IncrementLoad
Resolve     → IHumanAgentRegistry.DecrementLoad
TakeOver    → IncrementLoad
Release     → DecrementLoad
```

`CurrentLoad` agent'ın eş zamanlı konuşma sayısı — `MaxConcurrentLoad`'a yaklaşınca SkillsBasedRouter agent'a düşük score verir.

---

## AgentsEndpoints — `/agents` CRUD

| Route | Method | Açıklama |
|---|---|---|
| `/agents` | GET | Tüm agent'lar |
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

`/agents` GET endpoint **iki kaynak**'tan agent'ları birleştirir:

1. `IHumanAgentRegistry.GetAll()` — Agent registry
2. `IUserAuthRepository.FindByRole("Agent")` — Login yapan agent kullanıcılar

Bazı agent'lar registry'de var ama user yok (ör. eski kayıt). Bazıları user var ama LinkedAgentId set edilmemiş. Admin paneli ikisini görüp eşleştirir.

---

## Bağlantılar

- [Application ApprovalPortService](../application/ApprovalPortService.md)
- [Application EscalationPortService](../application/EscalationPortService.md)
- [Application ChatSessionPortService](../application/ChatSessionPortService.md)
- [Application HumanAgentPortService](../application/HumanAgentPortService.md)
- [Domain Model-Hitl](../domain/Model-Hitl.md)
- [Models.md](Models.md) — AdminModels DTO'lar

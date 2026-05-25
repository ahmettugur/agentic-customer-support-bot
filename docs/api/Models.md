# HTTP Models — DTO Katmanı

**Klasör:** `Models/`

HTTP isteklerinin **giriş şekli**. Domain modelleri doğrudan endpoint'lerde expose edilmez — bu DTO'lar **anti-corruption layer** rolü oynar.

| Dosya | İçerik |
|---|---|
| `AdminModels.cs` | Admin endpoint input DTO'ları |
| `Auth/AuthDtos.cs` | Login/refresh/logout |
| `EndpointModels.cs` | Çeşitli endpoint input'ları |
| `WorkflowRequest.cs` | Workflow CRUD request'i |

---

## Neden ayrı DTO?

Domain modelleri (ör. `WorkflowDefinition`) iş kuralları ve invariant'lar taşır. HTTP boundary'sinde:

- **Tip değişiklikleri** olabilir (enum string, string ID, vb.)
- **Validation farklı** — HTTP'de optional, Domain'de required olabilir
- **Versiyon değişikliği** Domain'i bozmamalı

DTO ↔ Domain dönüşümü endpoint içinde manuel yapılır (mapper kütüphanesi yok — basit ve görünür).

---

## AdminModels.cs

```csharp
public sealed record ChatTakeoverInput(string? HumanAgent);
public sealed record ChatAdminMessageInput(string Text, string? HumanAgent);
public sealed record ReplanInput(string? RequestedBy, string? Note);
```

### `ChatTakeoverInput`

`POST /chat-sessions/{sid}/takeover`

```json
{ "humanAgent": "Ali Demir" }
```

`HumanAgent` opsiyonel — null ise JWT'den çıkarılır.

### `ChatAdminMessageInput`

`POST /chat-sessions/{sid}/messages`

```json
{ "text": "Merhaba, size nasıl yardımcı olabilirim?", "humanAgent": "Ali Demir" }
```

### `ReplanInput`

`POST /chat-sessions/{sid}/replan`

```json
{ "requestedBy": "admin-abc", "note": "Müşteri farklı bir agent istiyor, OrderAgent'a yönlendir" }
```

`Note` LLM prompt'una eklenir — admin'in manuel yönlendirme isteği reasoning aşamasına girer.

---

## AuthDtos.cs

```csharp
public sealed record LoginRequest(string Username, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record LogoutRequest(string RefreshToken);
```

### Login flow

```http
POST /auth/login
Content-Type: application/json

{ "username": "admin", "password": "..." }
```

Response:
```json
{
  "accessToken": "eyJ...",
  "refreshToken": "abc-xyz-...",
  "accessTokenExpiry": "2026-05-24T11:00:00Z",
  "user": {
    "id": "u1",
    "username": "admin",
    "role": "Admin",
    "linkedAgentId": null
  }
}
```

### Refresh flow

```http
POST /auth/refresh
Content-Type: application/json

{ "refreshToken": "abc-xyz-..." }
```

Eski token revoke, yenisi döner. Rotation chain için detay: [Adapters.Persistence AuthAdapters](../adapters-persistence/AuthAdapters.md).

### Logout flow

```http
POST /auth/logout
Authorization: Bearer <access-token>
Content-Type: application/json

{ "refreshToken": "abc-xyz-..." }
```

Refresh token revoke edilir; access token JWT olduğu için süresi dolana kadar geçerli — kısa süreli (60 dakika) tutulur.

---

## EndpointModels.cs

Çeşitli endpoint'lerin input DTO'ları:

```csharp
public sealed record RerouteInput(string? AgentId, string? Reason);
public sealed record TestRunInput(string? Input, Dictionary<string, string>? Variables);
public sealed record ImprovementDecision(string? DecidedBy, string Reason);
public sealed record AdminNoteInput(string? Note);
public sealed record RatingInput(int Stars, string? Feedback);
```

### `RerouteInput`

`POST /escalations/{id}/reroute` — escalation'ı başka agent'a manuel yönlendirme.

```json
{ "agentId": "agent-2", "reason": "İlk agent meşgul, VIP müşteri" }
```

### `TestRunInput`

`POST /workflows/{id}/test` — workflow'u test verisiyle çalıştır.

```json
{
  "input": "5 nerede",
  "variables": { "customer_id": "1990" }
}
```

### `ImprovementDecision`

`POST /improvements/{id}/approve` veya `/reject`.

```json
{ "decidedBy": "admin-abc", "reason": "Faydalı bir ders, KB'ye eklensin" }
```

### `AdminNoteInput`

`PUT /customers/{id}/profile/note` — müşteri profiline manuel not.

```json
{ "note": "VIP müşteri, hızlı yanıt vermeli" }
```

`Note` `CustomerProfile.AdminNote` field'ına yazılır; agent prompt'unda görünür.

### `RatingInput`

`POST /sessions/{sid}/rating` — kullanıcının session'ı yıldızlaması.

```json
{ "stars": 4, "feedback": "İyi yardımcı oldu ama biraz yavaş" }
```

Validation: `stars` 1-5 arası olmalı; aksi halde 400 Bad Request.

---

## WorkflowRequest.cs

Workflow create/update için **detaylı** DTO:

```csharp
public sealed class WorkflowRequest
{
    public string Name { get; set; }
    public string? Description { get; set; }
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public List<string> TriggerKeywords { get; set; } = new();
    public Dictionary<string, string> InputPatterns { get; set; } = new();
    public List<WorkflowStepRequest> Steps { get; set; } = new();
}

public sealed class WorkflowStepRequest
{
    public string? Id { get; set; }
    public string Type { get; set; } = "Respond";    // Respond | Lookup | Branch | SetVariable
    public string? Label { get; set; }

    // Respond
    public string? Template { get; set; }

    // Lookup
    public string? Tool { get; set; }
    public Dictionary<string, string>? Parameters { get; set; }
    public string? StoreAs { get; set; }

    // Branch
    public string? Condition { get; set; }
    public int SkipNext { get; set; } = 1;

    // SetVariable
    public string? VariableName { get; set; }
    public string? VariableValue { get; set; }
}
```

### Neden ayrı DTO?

`WorkflowDefinition` (Domain) tipi `WorkflowStepType` enum kullanır. HTTP'de string olarak geliyor — DTO bunu string olarak tutar, endpoint'te dönüştürülür:

```csharp
var domain = new WorkflowDefinition
{
    Name = request.Name,
    Steps = request.Steps.Select(s => new WorkflowStep
    {
        Type = Enum.Parse<WorkflowStepType>(s.Type),
        Template = s.Template,
        // ...
    }).ToList()
};
```

Bu sayede:
- API consumer'ları string enum gönderir (daha kolay)
- Domain enum strict tip korur
- Yeni step tipi eklemek Domain'i etkilemez (DTO `string`, parse hata verirse 400 döner)

### Request örneği

```http
POST /workflows
Authorization: Bearer <admin-jwt>
Content-Type: application/json

{
  "name": "Sipariş Takibi",
  "description": "1030 siparişinin durumunu sorgular",
  "version": 1,
  "isActive": true,
  "triggerKeywords": ["takip", "kargoda", "nerede"],
  "inputPatterns": {
    "order_id": "\\d{4,}"
  },
  "steps": [
    {
      "type": "Branch",
      "label": "order_id var mı?",
      "condition": "order_id missing",
      "skipNext": 99
    },
    {
      "type": "Lookup",
      "label": "Sipariş durumu",
      "tool": "order_status_tool",
      "parameters": { "order_id": "{order_id}" },
      "storeAs": "status"
    },
    {
      "type": "Respond",
      "template": "Sipariş {order_id} durumu: {status}"
    }
  ]
}
```

Detay: [Domain Model-Workflow](../domain/Model-Workflow.md).

---

## DTO best practice

| Kural | Sebep |
|---|---|
| `sealed record` (immutable) | Threadsafe, predictable |
| Null safety (`string?`) | Optional field'lar açıkça belirtilir |
| Default değer | Forward-compat (yeni field eklenince eski client patlamaz) |
| Domain tipini doğrudan input alma | Domain invariant'ları bozulur |
| Validation endpoint'te | Açık ve görünür (FluentValidation gibi gizli katman yok) |

---

## Bağlantılar

- [Endpoints-Auth.md](Endpoints-Auth.md) — Login/Refresh akışı
- [Endpoints-Admin.md](Endpoints-Admin.md) — AdminModels kullanımı
- [Endpoints-Improvements.md](Endpoints-Improvements.md) — Workflow CRUD
- [Domain Model-Workflow](../domain/Model-Workflow.md)

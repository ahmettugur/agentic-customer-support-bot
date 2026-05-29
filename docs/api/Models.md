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

`PortAliases.cs` global using ile `CustomerSupportBot.Api.Models` tüm endpoint dosyalarında erişilebilir:

```csharp
global using CustomerSupportBot.Api.Models;
```

---

## AdminModels.cs

```csharp
public record ChatTakeoverInput(string? HumanAgent);
public record ChatAdminMessageInput(string Text, string? HumanAgent);
public record ReplanInput(string? RequestedBy, string? Note);
```

### `ChatTakeoverInput`

`POST /chat-sessions/{sid}/takeover` ve `POST /agent/chat-sessions/{sid}/takeover`

```json
{ "humanAgent": "Ali Demir" }
```

`HumanAgent` opsiyonel — null ise JWT claim veya default admin adı kullanılır.

### `ChatAdminMessageInput`

`POST /chat-sessions/{sid}/messages` ve `POST /agent/chat-sessions/{sid}/messages`

```json
{ "text": "Merhaba, size nasıl yardımcı olabilirim?", "humanAgent": "Ali Demir" }
```

`text` zorunlu, eksikse 400 döner.

### `ReplanInput`

`POST /escalations/{id}/replan`, `POST /chat-sessions/{sid}/replan` ve agent panel karşılıkları

```json
{ "requestedBy": "admin-abc", "note": "Müşteri farklı bir agent istiyor" }
```

`Note` LLM prompt'una eklenir — admin'in yönlendirme isteği reasoning aşamasına girer. Müşteriye gösterilmez.

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

### Refresh flow

```http
POST /auth/refresh
Content-Type: application/json

{ "refreshToken": "abc-xyz-..." }
```

### Logout flow

```http
POST /auth/logout
Authorization: Bearer <access-token>
Content-Type: application/json

{ "refreshToken": "abc-xyz-..." }
```

---

## EndpointModels.cs

Çeşitli endpoint'lerin input DTO'ları:

```csharp
// AgentsEndpoints
public class RerouteInput
{
    public string? AgentId { get; set; }
    public string? Reason { get; set; }
}

// WorkflowEndpoints
public class TestRunInput
{
    public string? Input { get; set; }
    public Dictionary<string, string>? Variables { get; set; }
}

// ImprovementsEndpoints
public sealed record ImprovementDecision(string? DecidedBy, string? Reason);

// PersonalizationEndpoints
public sealed class AdminNoteInput
{
    public string? Note { get; set; }
}

// AnalyticsEndpoints
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
{ "input": "5 nerede", "variables": { "customer_id": "1990" } }
```

### `ImprovementDecision`

`POST /improvements/{id}/approve` veya `/reject`.

```json
{ "decidedBy": "admin-abc", "reason": "Faydalı bir ders, KB'ye eklensin" }
```

`Reason` her iki DTO'da da opsiyonel (`string?`).

### `AdminNoteInput`

`PUT /customers/{id}/profile/note` — müşteri profiline manuel not.

```json
{ "note": "VIP müşteri, hızlı yanıt vermeli" }
```

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
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
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
    public string? Template { get; set; }             // Respond
    public string? Tool { get; set; }                 // Lookup
    public Dictionary<string, string> Parameters { get; set; } = new();
    public string? StoreAs { get; set; }              // Lookup
    public string? Condition { get; set; }            // Branch
    public int SkipNext { get; set; } = 1;            // Branch
    public string? VariableName { get; set; }         // SetVariable
    public string? VariableValue { get; set; }        // SetVariable
}
```

### Neden ayrı DTO?

`WorkflowDefinition` (Domain) tipi `WorkflowStepType` enum kullanır. HTTP'de string olarak geliyor — DTO bunu string olarak tutar, endpoint'te dönüştürülür:

```csharp
Type = Enum.TryParse<WorkflowStepType>(s.Type, true, out var t) ? t : WorkflowStepType.Respond,
```

Parse başarısız olursa `Respond` default. Bu sayede yeni step tipi eklemek Domain'i etkilemez.

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
  "inputPatterns": { "order_id": "\\d{4,}" },
  "steps": [
    { "type": "Branch", "label": "order_id var mı?", "condition": "order_id missing", "skipNext": 99 },
    { "type": "Lookup", "label": "Sipariş durumu", "tool": "order_status_tool",
      "parameters": { "orderId": "$order_id" }, "storeAs": "lookup" },
    { "type": "Respond", "template": "Sipariş {order_id} durumu: {lookup}" }
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
| Validation endpoint'te | Açık ve görünür |

---

## Bağlantılar

- [Endpoints-Auth.md](Endpoints-Auth.md) — Login/Refresh akışı
- [Endpoints-Admin.md](Endpoints-Admin.md) — AdminModels kullanımı
- [Endpoints-Improvements.md](Endpoints-Improvements.md) — Workflow CRUD, Memory, Personalization
- [Domain Model-Workflow](../domain/Model-Workflow.md)

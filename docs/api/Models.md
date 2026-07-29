# HTTP Models — DTO Katmanı

**Klasör:** `Models/`

HTTP isteklerinin **giriş şekli**. Domain modelleri doğrudan endpoint'lerde expose edilmez — bu DTO'lar **anti-corruption layer** rolü oynar.

| Dosya | İçerik |
|---|---|
| `AdminModels.cs` | Admin endpoint input DTO'ları |
| `Auth/AuthDtos.cs` | Login/refresh/logout |
| `EndpointModels.cs` | Çeşitli endpoint input'ları |

---

## Neden ayrı DTO?

Domain modelleri iş kuralları ve invariant'lar taşır. HTTP boundary'sinde:

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
- [Endpoints-Improvements.md](Endpoints-Improvements.md) — Improvements, Memory, Personalization

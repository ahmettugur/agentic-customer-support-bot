# Security — Güvenlik ve Kimlik Doğrulama

Bu doküman uygulamanın güvenlik katmanlarını, kimlik doğrulama akışını, girdi filtrelemeyi ve yan-etki korumasını anlatır.

---

## 1. JWT Authentication

Uygulama **JWT Bearer** kimlik doğrulaması kullanır. Token'lar `AuthEndpoints` üzerinden yönetilir.

### Akış

```
Kullanıcı → POST /auth/login { username, password }
         ← { accessToken, refreshToken, expiresAt }

Sonraki istekler → Authorization: Bearer <accessToken>

Token süresi dolunca → POST /auth/refresh { refreshToken }
                     ← { accessToken, refreshToken, expiresAt }

Çıkış → POST /auth/logout (Requires Auth)
```

### Konfigürasyon (`appsettings.json`)

```json
{
  "Jwt": {
    "Issuer": "CustomerSupportBot",
    "Audience": "CustomerSupportBot",
    "SigningKey": "DEV_ONLY_REPLACE_IN_PRODUCTION_with_at_least_32_random_chars_!",
    "AccessTokenMinutes": 60,
    "RefreshTokenDays": 14
  },
  "Auth": {
    "DefaultAdminUsername": "admin",
    "DefaultAdminPassword": "Admin123!"
  }
}
```

**Uyarı**: Production ortamında `SigningKey` mutlaka değiştirilmeli ve environment variable / user secrets üzerinden verilmelidir.

### SSE / EventSource Token Desteği

SSE bağlantıları HTTP header gönderemediği için, `access_token` query string parametresi olarak kabul edilir:

```
GET /chat/events/{sessionId}?access_token=<jwt>
```

Bu davranış `AuthServicesExtensions.cs` içinde `OnMessageReceived` event handler'ı ile sağlanır.

### Password Hashing

Parolalar **BCrypt** (`BCrypt.Net-Next`) ile hash'lenir. `IPasswordHasher` arayüzü üzerinden soyutlanmıştır.

---

## 2. Yetkilendirme (Authorization)

### Admin Policy

```csharp
// Program.cs
var adminScope = app.MapGroup("").RequireAuthorization("Admin");
```

`"Admin"` policy'si `RequireRole("Admin")` olarak tanımlanmıştır. Admin scope altındaki tüm endpoint'ler JWT ile korumalıdır:

| Scope | Endpoint'ler |
|-------|-------------|
| **Public** (auth gerektirmez) | `POST /chat/`, `POST /chat/stream`, `GET /chat/events/{sid}`, `GET /sessions/`, `POST/GET .../rating` |
| **Auth gerektirir** | `POST /auth/logout` |
| **Admin** | Trace, Evaluation, Memory, Improvements, Telemetry, Personalization, Agents, Workflows, SLA, Admin (HITL) |

### Rate Limiting

Chat endpoint'leri `"chat"` rate limiting policy'si altındadır. Default: IP başına dakikada 20 istek.

```csharp
app.MapPost("/chat/", HandleChatAsync).RequireRateLimiting("chat");
app.MapPost("/chat/stream", HandleChatStreamAsync).RequireRateLimiting("chat");
```

### CORS

Default CORS policy tüm origin, method ve header'lara izin verir (`AllowAnyOrigin`). Production ortamında kısıtlanmalıdır.

---

## 3. InputGuard — Girdi Güvenlik Filtresi

`InputGuard` servisi her chat isteğinde çalışır ve kullanıcı girdisini kontrol eder.

### Çalışma Akışı

```
Kullanıcı mesajı → InputGuard.Inspect(query)
    ├── Verdict: Pass    → sanitized input ile devam
    ├── Verdict: Flagged → log + sanitized input ile devam (flagler kaydedilir)
    └── Verdict: Reject  → 400 Bad Request (input_blocked)
```

### Kontrol Edilen Alanlar

- **Prompt injection** kalıpları ("ignore previous instructions", "system prompt" vb.)
- **Zararlı içerik** tespiti
- **Aşırı uzun girdi** kontrolü
- **Girdi sanitization** (zararsız hale getirme)

### Endpoint Davranışı

```json
// Reject durumunda
{
  "error": "input_blocked",
  "message": "Girdiniz güvenlik politikalarına uygun değil.",
  "flags": ["prompt_injection"]
}
```

---

## 4. Tool Güvenlik Katmanları

### 4.1 HITL Approval Gate

Yan etkili tool'lar (`order_placement_tool`, `complaint_registration_tool`) çalıştırılmadan önce admin onayı gerektirir.

```json
{
  "HumanInTheLoop": {
    "Enabled": true,
    "ToolsRequiringApproval": [
      "order_placement_tool",
      "complaint_registration_tool"
    ],
    "TimeoutSeconds": 60,
    "AutoApproveOnTimeout": false
  }
}
```

`ApprovalGateService`, tool lambda'larını sararak `Enabled=true` ise onay bekletir, `false` ise pass-through yapar. Timeout sonrası `AutoApproveOnTimeout` ayarına göre otomatik onay veya red uygulanır.

### 4.2 Tool Idempotency

Yan etkili tool'lar (`order_placement_tool`, `complaint_registration_tool`) için **SHA256 hash tabanlı idempotency cache** mevcuttur:

- Aynı parametrelerle 60 saniye içinde yapılan çağrılar cache'den döner
- Mükerrer kayıtlar (ör. compound query'de aynı şikayetin iki kez açılması) engellenir
- Cache max 200 entry tutar; eski entry'ler otomatik temizlenir

### 4.3 Workflow Yasak Tool'lar

Low-code Workflow Designer'da yan etkili tool'lar çağrılamaz:

| Yasak Tool | Neden |
|------------|-------|
| `order_placement_tool` | Sipariş oluşturur (yan etkili) |
| `complaint_registration_tool` | Şikayet kaydeder (yan etkili) |
| `human_handoff_tool` | İnsan aktarımı başlatır (yan etkili) |

Bu kısıtlama HITL approval gate'inin bypass edilmesini önler.

---

## 5. Workflow Guard'lar

```json
{
  "WorkflowGuards": {
    "TimeoutSeconds": 180,
    "MaxDuplicateToolCalls": 3,
    "MaxTokensPerRequest": 30000,
    "MaxIterations": 20
  }
}
```

| Guard | Ne yapar? |
|-------|-----------|
| **Timeout** | Tek workflow turunun max süresi. Aşılırsa iptal edilir |
| **MaxDuplicateToolCalls** | Aynı tool'u N kez arka arkaya çağırırsa devre kesilir |
| **MaxTokensPerRequest** | Bağlam toplam token üst sınırı |
| **MaxIterations** | ChatManager max agent geçiş sayısı |

---

## 6. Hassas Dosya Yönetimi

### `.gitignore`'da Korunan Dosyalar

```
appsettings.Development.json
appsettings.Production.json
```

### Dikkat Edilmesi Gerekenler

- `appsettings.json` içindeki `Jwt:SigningKey` ve `Auth:DefaultAdminPassword` **development-only** değerlerdir — production'da mutlaka değiştirilmelidir
- `ConnectionStrings` içindeki veritabanı parolaları environment variable ile override edilmelidir
- API key'ler **asla** kaynak kodda tutulmamalı — `dotnet user-secrets` veya environment variable kullanılmalıdır

---

## 7. Entity Verification (Grounded Reasoning)

`EntityVerifier` kullanıcı sorgusundaki ID'leri (ORD-1, CST-001 gibi) regex ile çıkarır ve `FakeDatabase`'e karşı doğrular. Bu sayede:

- LLM'in uydurduğu (hallucinated) ID'ler tespit edilir
- Doğrulanmış entity'ler reasoning'e `VerifiedEntities` olarak geçilir
- `ReasoningSanityChecker` (8 kural) reasoning çıktısını entity bilgisiyle kıyaslar

---

## Çapraz Referanslar

- **HITL pattern detayları** → [patterns.md](patterns.md#20-human-in-the-loop)
- **API endpoint güvenlik kapsamı** → [api.md](api.md)
- **Workflow guard'lar** → [workflow.md](workflow.md)
- **Mimari genel bakış** → [architecture.md](architecture.md)

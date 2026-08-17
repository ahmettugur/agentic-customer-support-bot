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

### Roller ve Policy'ler

Sistem iki kullanıcı rolü tanır:

| Rol | Açıklama | JWT `role` claim |
|-----|----------|-----------------|
| `Admin` | Tüm admin panel ve yönetim endpoint'lerine erişir | `"Admin"` |
| `Agent` | Yalnızca `/agent/*` endpoint grubuna erişir; kendi eskalasyonlarını ve onaylarını yönetir | `"Agent"` |

**`Agent` kullanıcılarında ek claim**: `linked_agent_id` — bu agent'ın `HumanAgent` registry'sindeki ID'si (ör. `"agent-jdoe"`). Eskalasyon atama ve filtreleme bu ID üzerinden yapılır.

```json
// Agent JWT payload örneği
{
  "sub": "42",
  "unique_name": "john.doe",
  "role": "Agent",
  "linked_agent_id": "agent-jdoe",
  "exp": 1747000000
}
```

### Admin Policy

```csharp
// Program.cs
var adminScope = app.MapGroup("").RequireAuthorization("Admin");
```

`"Admin"` policy'si `RequireRole("Admin")` olarak tanımlanmıştır.

### AdminOrAgent Policy

```csharp
var agentScope = app.MapGroup("/agent").RequireAuthorization("AdminOrAgent");
```

`"AdminOrAgent"` policy'si `RequireRole("Admin", "Agent")` olarak tanımlanmıştır. `/agent/*` endpoint'leri hem admin hem agent tarafından kullanılabilir.

### Endpoint Erişim Tablosu

| Scope | Endpoint'ler |
|-------|-------------|
| **Customer** | `POST /chat/`, `POST /chat/stream`, `GET /chat/events/{sid}`, `/chat-sessions/{sid}/approvals/*`, `GET /customer/approvals/history`, **`WS /chat/realtime/{sid?}`**, **`WS /chat/realtime-native/{sid?}`** |
| **Public** (auth gerektirmez) | `GET /sessions/`, `POST/GET .../rating` |
| **Auth gerektirir** | `POST /auth/logout` |
| **Admin** | Trace, Evaluation, Memory, Improvements, Telemetry, Personalization, Agents, SLA, Admin (HITL) |
| **AdminOrAgent** | `/agent/escalations/*`, `/agent/approvals/*`, `/agent/chat-sessions/*`, `/agent/profile` |

### Oturum ↔ müşteri bağı

Kimliği doğrulanan her kanal, oturumu login'li müşteriye bağlar: kimlik `linked_customer_id`
claim'inden okunur ve `SessionState.AuthenticatedCustomerId`'ye **bir kez** yazılır. Sipariş,
iptal, iade ve şikayet tool'ları `customerId`'yi yalnızca buradan alır — LLM'in kullanıcı
metninden çıkardığı numara asla kullanılmaz.

`SessionIdentityBinder` bu kuralı tek noktada tutar ve oturum **başka** bir müşteriye bağlıysa
erişimi reddeder.

### Neden kimlik doğrulama tek başına yetmiyor

`sessionId` **her zaman istemciden gelir** — URL yolunda ya da istek gövdesinde.
`RequireAuthorization("Customer")` yalnızca *"bu kişi bir müşteri mi"* sorusunu yanıtlar;
*"bu oturum onun mu"* sorusunu yanıtlamaz. İkincisi sorulmadığında, geçerli bir token'a sahip
herhangi bir müşteri başkasının `sessionId`'sini vererek:

- o oturumun **konuşma geçmişini** alabilir,
- **canlı olay akışını** dinleyebilir (bot yanıtları, onay sonuçları, temsilci mesajları),
- tool'ları o oturumun kimliğiyle çalıştırabilirdi (sipariş geçmişi, iptal, iade).

### İki katmanlı uygulama

| Katman | Nerede | Ne yapar |
|---|---|---|
| **Uç** (asıl koruma) | `ChatEndpoints.IsSessionAccessibleAsync` | `SessionIdentityBinder.IsAccessibleAsync` ile salt-okunur kontrol → **403** `session_forbidden` |
| **Port** (derinlemesine savunma) | `ChatPortService` → `SessionIdentityBinder.TryBindAsync` | İhlalde `UnauthorizedSessionAccessException` → `DomainExceptionHandler` → **403** |

İkinci katman gereksiz görünebilir ama değil: uç katmanındaki kontrolü kaldıran bir mutasyon
denendiğinde `POST /chat/` yine 403 döndü — port kendi başına da reddediyor. İleride bu port'u
çağıracak başka bir giriş (ör. A2A) uç kontrolünü atlarsa koruma yerinde kalır.

Salt-okunur uçlar (`IsAccessibleAsync`) oturumu **oluşturmaz ve değiştirmez**; aksi hâlde
rastgele `sessionId` veren biri sınırsız boş oturum üretebilirdi. Yazma yolları
(`TryBindAsync`) ise oturum sahipsizse onu çağırana bağlar.

### Korunan uçlar

| Uç | Kontrol |
|---|---|
| `POST /chat/` | 403 JSON |
| `POST /chat/stream` | SSE header'ları yazıldıktan sonra durum kodu değişemez → akış içinde `session_forbidden` olayı, tur hiç başlamaz |
| `GET /chat/events/{sid}` | 403 (header yazılmadan önce) |
| `GET /chat-sessions/{sid}/approvals/unseen` | 403 JSON |
| `POST /chat-sessions/{sid}/approvals/{id}/seen` | 403 JSON |
| `WS /chat/realtime*` | Bağlantı `SessionIdentityBinder` ile reddedilir |
| `GET /customer/approvals/history` | `sessionId` almaz — kimlik doğrudan JWT claim'inden, sahiplik sorusu doğmaz |

Davranış `ChatSessionOwnershipTests` (uçtan uca HTTP) ve `SessionIdentityBinderTests`
(birim) ile korunur.

### WebSocket'lerde token taşıma

Tarayıcı WebSocket handshake'ine `Authorization` header'ı ekleyemez. Bu yüzden realtime uçları
token'ı query string'den alır (`?access_token=…`); `AuthServicesExtensions.OnMessageReceived`
bunu bearer token olarak okur. Aynı mekanizmayı SSE (`EventSource`) de kullanır.

> Token'ın URL'de taşınması sunucu erişim loglarına düşebilir. Kabul edilmesinin sebebi
> alternatifin (kısa ömürlü tek kullanımlık bilet ucu) ek bir uç ve durum yönetimi
> gerektirmesi; token ömrü zaten kısadır ve refresh akışı mevcuttur.

### Rate Limiting

| Policy | Limit | Kapsam |
|--------|-------|--------|
| `chat` | IP başına 20/dk | `POST /chat/`, `POST /chat/stream` |
| `general` | IP başına 60/dk | `/analytics/*` (public) + tüm `Admin`/`AdminOrAgent` scope'ları (`adminScope`, `agentScope` — Program.cs) |

```csharp
app.MapPost("/chat/", HandleChatAsync).RequireRateLimiting("chat");
app.MapPost("/chat/stream", HandleChatStreamAsync).RequireRateLimiting("chat");

var adminScope = app.MapGroup("").RequireAuthorization("Admin").RequireRateLimiting("general");
var agentScope = app.MapGroup("").RequireAuthorization("AdminOrAgent").RequireRateLimiting("general");
```

> Admin/agent uçları auth arkasında olsa da önceden rate limitsizdi — sızmış bir JWT veya kötü niyetli bir admin/agent hesabı sınırsız istek atabiliyordu. `general` politikası grup seviyesinde uygulanır; SSE endpoint'leri (`/chat-sessions/{sid}/subscribe` vb.) tek bir istek olarak sayıldığından uzun ömürlü bağlantılar limitten etkilenmez.

### CORS

Default CORS policy tüm origin, method ve header'lara izin verir (`AllowAnyOrigin`). Production ortamında kısıtlanmalıdır.

---

## 3. InputGuard — Girdi Güvenlik Filtresi

`InputGuard` servisi her chat isteğinde çalışır ve kullanıcı girdisini kontrol eder.

### Çalışma Akışı

```
Kullanıcı mesajı → InputGuard.Inspect(query)
    ├── Verdict: Allow    → sanitized input ile devam
    ├── Verdict: Sanitize → tanımlı ama Inspect() tarafından hiç üretilmiyor (ölü dal)
    └── Verdict: Reject   → 400 Bad Request (input_blocked)
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

Veritabanına **yazan dört tool** çalıştırılmadan önce admin onayı gerektirir. (Diğer tool'lar — `order_status_tool`, `get_last_order_tool`, `get_all_orders_tool`, `product_inquiry_tool`, `product_list_tool`, `human_handoff_tool` — salt okuma yapar veya DB'ye hiç dokunmaz, onay gerektirmez.)

```json
{
  "HumanInTheLoop": {
    "Enabled": true,
    "ToolsRequiringApproval": [
      "order_placement_tool",
      "order_cancel_tool",
      "return_request_tool",
      "complaint_registration_tool"
    ],
    "TimeoutSeconds": 60,
    "AutoApproveOnTimeout": false
  }
}
```

`ApprovalGateService`, tool lambda'larını sararak `Enabled=true` ise onay bekletir, `false` ise pass-through yapar. Timeout sonrası `AutoApproveOnTimeout` ayarına göre otomatik onay veya red uygulanır.

#### Yüksek risk / gerekçe zorunluluğu — tek doğruluk kaynağı

Yukarıdaki dört tool aynı zamanda **yüksek riskli** sayılır: onaylanırken admin'in gerekçe (audit trail) yazması zorunludur (`AdminEndpoints`, `AgentPanelEndpoints` → 400 `approval_reason_required`).

Bu eşleme **yalnızca** `WellKnown.SideEffectToolOwners` sözlüğünde tanımlıdır (tool → sahibi ajan). Ondan türetilenler:

| Türetilen | Kullanan |
|---|---|
| `WellKnown.HighRiskTools` | gerekçe zorunluluğu kontrolü |
| `WellKnown.SideEffectToolsOf(agent)` | `WorkflowRunner.EnsureSideEffectToolCompletion` |
| `ApprovalGateService.ResolveAgentName` | admin panelinde "hangi ajan istiyor" |
| `ApprovalRequest.ReasonRequired` (serileşir) | Blazor admin paneli |

> ⚠️ Yeni bir yazma tool'u eklenirken **`SideEffectToolOwners` ve `appsettings` → `ToolsRequiringApproval`** güncellenir; başka hiçbir yerde liste tutulmaz.
>
> Bu yapı bir üretim bug'ından sonra kuruldu: admin paneli kendi `HighRiskTools` kopyasını tutuyordu ve `order_cancel_tool` + `return_request_tool` eklendiğinde güncellenmeyi kaçırdı. Panel bu tool'lar için gerekçeyi *"isteğe bağlı"* gösteriyor, admin boş bırakınca backend 400 dönüyor, HITL workflow'u duraklattığı için müşterinin iptal/iade talebi timeout'a kadar asılı kalıyordu. Panel artık liste tutmuyor, `ApprovalRequest.ReasonRequired` bayrağını uyguluyor. İnvariant `WellKnownTests` ile kilitli.

### 4.2 Tool Idempotency

**Dosya:** `CustomerSupportBot.Application/Services/Tools/SideEffectIdempotencyCache.cs`

Yan etkili tool'lar (`order_placement_tool`, `complaint_registration_tool`) için **SHA256 imza tabanlı mükerrer çağrı koruması** vardır:

- İmza = `toolAdı + parametreler`; 60 saniyelik pencere (`DefaultWindow`), max 200 kayıt (`DefaultMaxEntries`), aşılırsa en eski kayıtlar atılır
- Cache isabetinde tool **çalıştırılmaz** — stok düşülmez, DB'ye kayıt yazılmaz
- **Sonuç sessizce taklit edilmez.** Çağırana ayırt edilebilir bir bilgilendirme döner:
  *"Bu siparişi az önce oluşturmuştum — sipariş numarası: 1082. Mükerrer kayıt oluşturmadım. Gerçekten ikinci bir sipariş istiyorsanız lütfen açıkça belirtin."*
  `Data` içinde `duplicate = true` bayrağı bulunur. Böylece mükerrer kayıt engellenirken meşru tekrar talebi de kaybolmaz.
- Yalnızca **başarılı** çağrılar cache'lenir — hata sonrası yeniden deneme engellenmez
- İmza kanonik değerler üzerinden kurulur: sipariş için katalogdan çözülen ürün adı (`"kahve"` ve `"Kahve"` aynı sayılır), şikayet için türetilmiş `customerId` (parametrenin verilip verilmemesi iki farklı çağrı gibi görünmez)
- `SideEffectIdempotencyCache` **Singleton** kaydedilir; `OrderToolsService` ve `ComplaintToolsService` aynı örneği paylaşır

> **Neden gerekli?** `MaxDuplicateToolCalls` guard'ı `CustomerSupportChatManager` içinde, yani **tek workflow koşusunun** mesaj geçmişine bakar. Compound query'de her alt görev ayrı (bazen paralel) bir workflow koşusu olduğu için o guard mükerrer yan etkili çağrıları göremez. Bu cache o boşluğu kapatır.

---

## 5. Workflow Guard'lar

```json
{
  "WorkflowGuards": {
    "TimeoutSeconds": 180,
    "MaxDuplicateToolCalls": 3,
    "MaxIterations": 20
  }
}
```

| Guard | Ne yapar? |
|-------|-----------|
| **Timeout** | Tek workflow turunun max süresi. Aşılırsa iptal edilir — hem `RunStreamingAsync` hem `RunAsync` (non-streaming: `EvaluationRunner`, `ReplanService`) için geçerlidir |
| **MaxDuplicateToolCalls** | Aynı tool'u N kez arka arkaya çağırırsa devre kesilir |
| **MaxIterations** | ChatManager max agent geçiş sayısı |

> **Kaldırıldı:** `MaxTokensPerRequest` daha önce config'de tanımlıydı ama kodda hiç okunmuyordu (ölü config). Workflow boyunca kümülatif token kullanımını izleyip orta-akışta kesmek `CustomerSupportChatManager`'a yeni bir mekanizma eklemeyi gerektiren ayrı bir özellik — var olmayan bir korumayı config'de var gibi göstermek yerine kaldırıldı. Gerçek token/maliyet takibi `TelemetryChatClient` + `CostUsageStore` üzerinden çağrı bazında yapılıyor (bkz. [architecture.md](architecture.md)).

`WorkflowGuardOptions` (ve `ApprovalOptions`, `ParallelExecutionOptions`) `ValidateOnStart()` ile kayıtlıdır — `TimeoutSeconds=0` gibi geçersiz bir değer artık ilk isteği değil **uygulama başlangıcını** patlatır.

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

`EntityVerifier` kullanıcı sorgusundaki ID'leri (1, CST-001 gibi) regex ile çıkarır ve repository port'ları üzerinden doğrular. Bu sayede:

- LLM'in uydurduğu (hallucinated) ID'ler tespit edilir
- Doğrulanmış entity'ler reasoning'e `VerifiedEntities` olarak geçilir
- `ReasoningSanityChecker` (8 kural) reasoning çıktısını entity bilgisiyle kıyaslar

---

## Çapraz Referanslar

- **HITL pattern detayları** → [agentic-patterns.md](agentic-patterns.md#20-human-in-the-loop)
- **API endpoint güvenlik kapsamı** → [CustomerSupportBot.Api/README.md](CustomerSupportBot.Api/README.md)
- **Mimari genel bakış** → [architecture.md](architecture.md)

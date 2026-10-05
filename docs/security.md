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

**Başlangıç kapıları** (uygulama açılmaz):

- **Tüm ortamlarda:** anahtar boş, 32 bayttan kısa ya da 12'den az farklı karakter içeriyorsa
  (`JwtOptions.ValidateSigningKey`; üreten ve doğrulayan taraf aynı kuralı kullanır). Eskiden boş
  anahtarda JwtBearer sabit bir yedekle (`'x' × 32`) doğrulama yapıyor, hata ancak ilk login'de
  çıkıyordu — o ana kadar herkesin bildiği o anahtarla imzalanmış token'lar geçerliydi.
- **Development dışında:** anahtar depodaki yer tutucuyu (`DEV_ONLY`, `REPLACE_IN_PRODUCTION`,
  `CHANGE_ME`, `PLACEHOLDER`) içeriyorsa.

### Refresh token rotasyonu ve yeniden kullanım tespiti

Her `/auth/refresh` çağrısı eski refresh token'ı **koşullu** iptal eder (`WHERE RevokedAt IS NULL`)
ve yenisini verir; eşzamanlı ikinci kullanım yeni token alamaz. Rotasyonla iptal edilmiş bir token
`ReuseGracePeriod`'dan (30 sn) sonra tekrar sunulursa bu bir **kopyalanma işaretidir** —
kullanıcının tüm aktif refresh token'ları iptal edilir ve `[Security]` uyarısı loglanır. Logout ile
iptal edilmiş token'lar ve 30 sn içindeki tekrarlar (aynı localStorage'ı paylaşan sekmelerin
eşzamanlı yenilemesi) yalnızca reddedilir. Logout da koşullu iptal kullanır. Ayrıntı:
[TokenPortService](CustomerSupportBot.Application/Services/Auth/TokenPortService.md).

İstemci tarafında (`AuthService`) aynı scope için yalnızca bir refresh uçuşta olabilir (kayıt ilk
`await`'ten önce) ve reddedilen bir refresh, başka sekmenin yeni token'ı depodaysa onu kullanır —
istemci kendi eski token'ını ikinci kez sunup yeniden kullanım tespitini tetiklemez.

### SSE / EventSource Token Desteği

SSE bağlantıları HTTP header gönderemediği için, `access_token` query string parametresi olarak kabul edilir:

```
GET /chat/events/{sessionId}?access_token=<jwt>
```

Bu davranış `AuthServicesExtensions.cs` içinde `OnMessageReceived` event handler'ı ile sağlanır ve **yalnızca header taşıyamayan istemcilerin uçlarında** geçerlidir (`AcceptsQueryStringToken`): `/chat/events/*`, `…/chat-sessions/{sid}/subscribe` ve `/chat/realtime-native/*`. Diğer tüm uçlar URL'deki token'ı yok sayar (401). Eskiden bu kabul tüm uçlara açıktı; yeni bir SSE/WS ucu eklenirse listeye de eklenmeli (bkz. `QueryStringTokenScopeTests`).

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
| **Customer** | `POST /chat/`, `POST /chat/stream`, `GET /chat/events/{sid}`, `/chat-sessions/{sid}/approvals/*`, `GET /customer/approvals/history`, **`WS /chat/realtime-native/{sid?}`** |
| **SessionAccess** (`Customer`, `Admin` veya `Agent`) | `GET /sessions/`, `GET /sessions/{sid}/messages`, `GET /sessions/{sid}/state` |
| **Public** (auth gerektirmez) | `POST/GET /sessions/{sid}/rating` |
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

### `sessionId` biçimi

Oturuma dokunmadan önce biçim doğrulanır (`SessionIdPolicy`): 1–64 karakter (DB kolonu
`varchar(64)`), yalnızca ASCII harf, rakam, `-` ve `_`. Sunucunun ürettiği `Guid` biçimi her
zaman geçerlidir. İhlal **400** `invalid_session_id` döner (SSE'de akış içinde olay; WS'de
soket kabul edilmeden önce 400). Eskiden doğrulama yoktu: 64 karakterden uzun bir id önce
cache'e ekleniyor, ardından DB yazması patlıyordu — tur ham bir DB hatasıyla bitiyor, cache'te
kaydedilmemiş bir oturum kalıyordu (derinlemesine savunma olarak `PostgresSessionManager`
başarısız oluşturmada nesneyi cache'ten de çıkarır). Kural oturum oluşturan/bağlayan uçlarda
uygulanır: `POST /chat/`, `POST /chat/stream`, `GET /chat/events/{sid}`, onay bildirim uçları ve
iki realtime WS ucu (bkz. `SessionIdValidationTests`).

### Korunan uçlar

| Uç | Kontrol |
|---|---|
| `POST /chat/` | 403 JSON |
| `POST /chat/stream` | SSE header'ları yazıldıktan sonra durum kodu değişemez → akış içinde `session_forbidden` olayı, tur hiç başlamaz |
| `GET /chat/events/{sid}` | 403 (header yazılmadan önce) |
| `GET /chat-sessions/{sid}/approvals/unseen` | 403 JSON |
| `POST /chat-sessions/{sid}/approvals/{id}/seen` | 403 JSON |
| `WS /chat/realtime-native/*` | Bağlantı `SessionIdentityBinder` ile reddedilir |
| `GET /customer/approvals/history` | `sessionId` almaz — kimlik doğrudan JWT claim'inden, sahiplik sorusu doğmaz |

Davranış `ChatSessionOwnershipTests` (uçtan uca HTTP) ve `SessionIdentityBinderTests`
(birim) ile korunur.

### WebSocket'lerde token taşıma

Tarayıcı WebSocket handshake'ine `Authorization` header'ı ekleyemez. Bu yüzden realtime uçları
token'ı query string'den alır (`?access_token=…`); `AuthServicesExtensions.OnMessageReceived`
bunu bearer token olarak okur. Aynı mekanizmayı SSE (`EventSource`) de kullanır.

> Token'ın URL'de taşınması sunucu erişim loglarına düşebilir. Kabul edilmesinin sebebi
> alternatifin (kısa ömürlü tek kullanımlık bilet ucu) ek bir uç ve durum yönetimi
> gerektirmesi; token ömrü zaten kısadır ve refresh akışı mevcuttur. Riski daraltmak için URL
> token'ı yalnızca bu SSE/WS uçlarında kabul edilir, header taşıyabilen uçlarda yok sayılır.

### Rate Limiting

| Policy | Limit | Kapsam |
|--------|-------|--------|
| `chat` | **Müşteri başına** 20/dk (`linked_customer_id`; claim yoksa IP) | `POST /chat/`, `POST /chat/stream`, `WS /chat/realtime-native/{sid?}` |
| `general` | IP başına 60/dk | Tüm `Admin`/`AdminOrAgent` scope'ları (`adminScope`, `agentScope` — Program.cs) + `/analytics/*` (ayrı map edildiği için **kendi başına** `RequireRateLimiting` taşır, `admin`/`agentScope` grubuna dahil DEĞİL) + `GET /sessions/*` + `GET /chat/events/{sid}`, `.../approvals/unseen`, `.../approvals/{id}/seen`, `GET /customer/approvals/history` |
| `a2a` | **Partner başına** `A2A:RequestsPerMinute` | `/a2a/*` |
| `auth` | IP başına `Jwt:AuthRateLimitPerMinute` (varsayılan 10/dk) | `/auth/*` (login, customer/login, customer/register, refresh, logout) |

```csharp
app.MapPost("/chat/", HandleChatAsync).RequireRateLimiting("chat");
app.MapPost("/chat/stream", HandleChatStreamAsync).RequireRateLimiting("chat");

var adminScope = app.MapGroup("").RequireAuthorization("Admin").RequireRateLimiting("general");
var agentScope = app.MapGroup("").RequireAuthorization("AdminOrAgent").RequireRateLimiting("general");
```

> **`a2a` politikası neden IP değil partner bazlı?** Dış sistemler proxy/bulut çıkışı arkasında IP paylaşabilir (bir partnerin trafiği diğerinin kotasını tüketirdi) ya da IP değiştirebilir (sınır fiilen ortadan kalkardı). Bölümleme anahtarı token'dan çıkarılır ve çağıran onu değiştiremez. Bunun çalışması **middleware sırasına bağlıdır**: `UseRateLimiter()` `UseAuthentication()`'dan SONRA gelmek zorundadır, aksi hâlde `HttpContext.User` henüz boştur, claim bulunamaz ve politika sessizce IP'ye düşer — kural "partner başına" yazılmış olsa bile fiilen IP başına çalışırdı.

> Admin/agent uçları auth arkasında olsa da önceden rate limitsizdi — sızmış bir JWT veya kötü niyetli bir admin/agent hesabı sınırsız istek atabiliyordu. `general` politikası grup seviyesinde uygulanır; SSE endpoint'leri (`/chat/events/{sid}` vb.) tek bir istek olarak sayıldığından uzun ömürlü bağlantılar limitten etkilenmez — sınırlanan, yeni bağlantı AÇMA hızıdır.

> **Realtime WS ucu (`/chat/realtime-native`) önceden TAMAMEN limitsizdi** — her bağlantı gerçek bir OpenAI Realtime API oturumu açtığı (yazılı chat'ten daha maliyetli) için bu, geçerli/sızmış bir müşteri JWT'siyle doğrudan maliyet-bombası DoS'una açık kapıydı. `sessions/*`, `chat/events`, `chat-sessions/*/approvals/*` ve `customer/approvals/history` de aynı şekilde auth arkasında ama limitsizdi; hepsine `general` uygulandı.
>
> **Bilinen sınır:** tüm policy'ler `FixedWindowLimiter` kullanır (sliding window değil) — bir istemci pencerenin son saniyesinde N istek, hemen ardından yeni pencerenin ilk saniyesinde bir N istek daha göndererek kısa bir aralıkta ~2N isteğe kadar çıkabilir. Bypass değil ama sınırı gevşetir; bilinçli bir trade-off (basitlik/performans), sıkılaştırma istenirse `SlidingWindowLimiter`'a geçilebilir.

> **`chat` neden müşteri başına?** Korunan şey her turdaki LLM maliyetidir. IP anahtarında aynı NAT/kurumsal çıkış arkasındaki müşteriler tek 20/dk kotasını paylaşıyor (biri diğerlerini kilitliyor), IP değiştirebilen tek bir hesap ise sınırı dolaşıyordu. Uçlar zaten Customer token'ı istediği için anahtar `customer:{linked_customer_id}` olur; aynı `a2a` gerekçesiyle limiter kimlik doğrulamadan SONRA çalışır.

> **Ters proxy arkasında IP tabanlı politikalar:** `auth` ve `general` `Connection.RemoteIpAddress`'e bakar; load balancer arkasında bu her istek için proxy'nin adresidir. `ForwardedHeaders:KnownProxies` (IP listesi) veya `ForwardedHeaders:KnownNetworks` (CIDR listesi) yapılandırıldığında boru hattının en başında `UseForwardedHeaders` devreye girer ve `X-Forwarded-For`/`X-Forwarded-Proto` **yalnızca bu karşı uçlardan** kabul edilir. Hiçbiri yapılandırılmamışsa middleware eklenmez. `ASPNETCORE_FORWARDEDHEADERS_ENABLED` bilinçli olarak kullanılmıyor: o yol güvenilir proxy listesini temizler ve her karşı ucun başlığına güvenir, dolayısıyla her istekte farklı değer gönderen bir istemci IP sınırını dolaşabilirdi.

> `/auth/*` önceden TAMAMEN sınırsızdı — bu uçlar AllowAnonymous olduğu için kimlik bilgisi tahmin etme (credential stuffing/brute force) ve kayıt spam'i tek istemciden ucu bucaksız denenebiliyordu. IP tabanlı: bu uçlarda henüz doğrulanmış bir kimlik yok, `a2a`'daki gibi bir claim mevcut değil.

### CORS

Default politika `Cors:AllowedOrigins` listesinden kurulur. Liste boşsa davranış ortama bağlıdır:

| Ortam | Boş liste |
|---|---|
| Development | `AllowAnyOrigin` (Blazor :5288 ↔ API :5021 kolaylığı) |
| Diğer | Hiçbir cross-origin çağırana izin verilmez; başlatmada `[CORS]` uyarısı loglanır |

Eskiden boş liste her ortamda `AllowAnyOrigin`'e düşüyordu. Depodaki varsayılan `appsettings.json`'da liste boş olduğu için üretim override'ı unutulduğunda API her siteden tarayıcı üzerinden çağrılabiliyordu.

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
- **PII maskeleme** — e-posta, telefon, TC kimlik no, kredi kartı deseni mesajda geçiyorsa
  reddetmeden **maskeler** (`pii_masked:*` flag'i eklenir). Maskelenen metin hem LLM'e
  (OpenAI) giden hem `ReasoningTrace.UserQuery`'ye kalıcı yazılan metinle **aynıdır** — yani
  bir müşteri yanlışlıkla kart/TC numarasını chat'e yazarsa bu bilgi üçüncü parti LLM
  sağlayıcısına ham gitmez, kalıcı trace'e de düşmez. Detay: [`InputGuard`](CustomerSupportBot.Application/Services/Chat/InputGuard.md),
  [`PiiMasker`](CustomerSupportBot.Application/Services/Logging/PiiMasker.md).

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
    "StalePendingHours": 72,
    "TimeoutSeconds": 60,
    "AutoApproveOnTimeout": false
  }
}
```

`ApprovalGateService`, tool lambda'larını sararak `Enabled=true` ise onay kaydı oluşturur, `false` ise pass-through yapar.

#### Onay BLOKLAMAZ

Tool çağrısı admin kararını **beklemez**. Onay gerekiyorsa kayıt oluşturulur ve tool hemen
"talebiniz onaya gönderildi" sonucunu döner; kullanıcının turu orada biter ve sohbete devam
edebilir. Gerçek iş (iptal, iade...) admin karar verdiğinde `IApprovalExecutionRouter` üzerinden
ayrıca tetiklenir; sonucu kullanıcıya bildirim/badge olarak ulaşır.

Bunun güvenlik açısından önemi: bekleyen bir onayın süreç-içi sahibi yoktur, bu yüzden yük
altında veya restart sonrasında sessizce kaybolmaz — kalıcı olarak PostgreSQL'de durur.

| Ayar | Bu dört tool için geçerli mi | Not |
|---|---|---|
| `StalePendingHours` (72) | ✅ | Bu süreyi aşan `Pending` kayıtlar `StaleApprovalSweepService` tarafından otomatik reddedilir. |
| `TimeoutSeconds` (60) | ❌ | Yalnızca eski bloklayan yol (`AwaitDecisionAsync`) için anlamlıydı; bu tool'lar artık o yolu kullanmıyor. |
| `AutoApproveOnTimeout` | ❌ | Aynı sebeple uygulanmaz. Bir talep zaman aşımına uğradığında sonuç **her zaman red**'dir. |

> Bu iki ayara bakıp "60 saniyede otomatik karar var" varsaymayın — yok. Bir talep, admin karar
> verene veya 72 saati doldurana kadar bekler.

#### Karar ile yürütme ayrı izlenir

`Status` (Pending/Approved/Rejected/Expired) admin'in kararıdır; `ExecutionStatus`
(None/Running/Succeeded/Failed) o kararın hayata geçip geçmediğidir. `Approved + Running`
durumunda kalmış bir kayıt, yürütme sırasında sürecin kapandığı anlamına gelir ve admin
panelinde uyarıyla işaretlenir. **Sistem bunu kendiliğinden tekrar denemez** — bu dört tool
idempotent olmadığı için otomatik retry, mükerrer iade/iptal riski taşır; doğrulama elle yapılır.

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

## 5b. Müşteri Fotoğrafları

Sohbete eklenen fotoğraflar (bkz. [Attachments](CustomerSupportBot.Application/Services/Attachments/README.md)):

- **Tür dosya imzasından** belirlenir (yalnızca JPEG/PNG); uzantı ya da istemcinin bildirdiği tür
  dikkate alınmaz. Görüntü yanıtları `nosniff` ve `Cache-Control: private` taşır.
- **Meta veri saklamadan ve görsel modele göndermeden önce silinir** — EXIF GPS konumu, cihaz,
  zaman, XMP, yorumlar, PNG metin parçaları. Yalnızca yön bilgisi korunur.
- Görsel modelin açıklaması kullanıcının yazdığı metinle aynı `InputGuard`'dan geçer: kişisel veri
  maskelenir, enjeksiyon olarak reddedilirse açıklama kullanılmaz. Talimat ayrıca kişisel veriyi
  yazıya dökmemeyi ve fotoğraftaki yazıları talimat saymamayı ister. Açıklama ajanlara kullanıcı
  rolünde gider — kullanıcının kendi yazdığıyla aynı güven düzeyi.
- Yükleme sohbetle aynı kurallara tabidir: Customer yetkisi, oturum sahipliği, `chat` hız sınırı;
  uçta gövde sınırı (12 MB), serviste dosya (5 MB) ve oturum (10) sınırı.
- Mesajdaki başka oturuma/müşteriye ait kimlikler yok sayılır; müşteri yalnızca kendi fotoğrafını
  okur. Onay kaydına yalnızca müşterinin **gönderdiği** fotoğraflar bağlanır.
- Fotoğraflar oturumla birlikte silinir (FK cascade); gönderilmemiş fotoğrafı müşteri silebilir.

## 5c. Kişisel Veri Saklama ve Silme (KVKK)

Bkz. [Privacy](CustomerSupportBot.Application/Services/Privacy/README.md).

- **Saklama süresi:** son etkinliği 180 günü (varsayılan) geçen oturumlar ve 90 günden eski fotoğraflar
  otomatik silinir (`DataRetention`). Silme oturuma bağlı tüm depoları kapsar: mesajlar, fotoğraflar, canlı
  devralma mesajları, puan/yorum, eskalasyon metni, akıl yürütme izleri, episodik bellek.
- **Önbellekler:** silinen veri tüm pod'ların bellek içi önbelleğinden de çıkarılır (`csbot:privacy:sessions-erased`).
- **Müşteri hakları:** müşteri sohbet ekranından verisini indirir ve siler; yönetici aynısını KVKK başvurusu için
  yapar (yalnız Admin). Silme `confirm=true` ister, denetim izi loglanır, içerik loglanmaz.
- **Kalanlar:** sipariş, şikayet ve onay kayıtları yasal/işlemsel kayıt olarak tutulur; açık eskalasyonlar
  temsilci kuyruğu bozulmasın diye silinmez, müşteri metni temizlenir.

## 5d. E-posta

- SMTP parolası repoya yazılmaz: `appsettings.json`'da boştur; `Email__Smtp__Password` ortam değişkeni ya da
  secret ile verilir. Mümkünse `StartTls`/`SslOnConnect` kullanılır (`None` yalnız yerel test sunucusu için).
- E-posta gövdesine giren sonuç/gerekçe metinleri HTML-escape edilir; loglarda alıcı adresi maskelenir.
- Bildirim defteri (`notifications.sent_log`) yalnızca anahtar tutar (`approval-result:{id}`), kişisel veri içermez.

## 6. Hassas Dosya Yönetimi

### `.gitignore`'da Korunan Dosyalar

```
appsettings.Development.json
appsettings.Production.json
```

### Dikkat Edilmesi Gerekenler

- `appsettings.json` içindeki `Jwt:SigningKey` ve `Auth:DefaultAdminPassword` **development-only** değerlerdir — production'da mutlaka değiştirilmelidir
- `ConnectionStrings` içindeki veritabanı parolaları environment variable ile override edilmelidir
- Yerel Docker yığınının gizli değerleri `deploy/.env`'dedir (`.gitignore`'da `.env`; şablon
  `deploy/.env.example`). Postgres parolası eskiden `deploy/docker-compose.yml`'de açık metin
  olarak commit edilmişti; git geçmişinde durduğu için o parola değiştirilmelidir.
- Yerel yığının portları yalnızca `127.0.0.1`'e açıktır; Redis/Qdrant/Elasticsearch orada
  kimlik doğrulamasızdır (bkz. [deployment.md](deployment.md))
- API key'ler **asla** kaynak kodda tutulmamalı — `dotnet user-secrets` veya environment variable kullanılmalıdır

---

## 7. Entity Verification (Grounded Reasoning)

`EntityVerifier` kullanıcı sorgusu ve geçmişindeki ID'leri regex + context continuity ile çözümler;
müşteri kimliğini yalnız authenticated session'dan alır. Sipariş/şikayet gerçekliği ve sahipliği
ise ilgili specialist tool'da doğrulanır. Bu sayede:

- Kullanıcının metinde yazdığı customer ID authenticated kimliği ezemez
- Sağlanan ID tekrar sorulmaz, fakat tool sonucu olmadan kayıt varmış gibi anlatılmaz
- Başka müşteriye ait status/ürün/şikayet attribute'ları reasoning prompt'una taşınmaz
- `ReasoningSanityChecker` (8 kural) reasoning çıktısını entity bilgisiyle kıyaslar

---

## Çapraz Referanslar

- **HITL pattern detayları** → [agentic-patterns.md](agentic-patterns.md#20-human-in-the-loop)
- **API endpoint güvenlik kapsamı** → [CustomerSupportBot.Api/README.md](CustomerSupportBot.Api/README.md)
- **Mimari genel bakış** → [architecture.md](architecture.md)

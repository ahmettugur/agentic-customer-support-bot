# Program.cs — Bootstrap

**Dosya:** `Program.cs`

Uygulamanın **Composition Root**'u. Tüm DI kayıtları, middleware pipeline'ı ve endpoint mapping burada.

---

## DI sıralaması

```
1. AddOpenApi()
2. AddLogging()
3. AddProblemDetails()
4. AddExceptionHandler<DomainExceptionHandler>()
5. AddTelemetryServices(config)         ← OpenTelemetry önce — diğerleri instrument edilebilsin
6. AddAiServices(config)                ← IChatClient + telemetry wrap
7. AddRedisServices(config)             ← Distributed lock + message bus
8. AddPersistenceServices(config)       ← EF Core veya InMemory
9. AddApplicationServices(config)       ← Port servisleri, CORS, rate limiting, hosted services
10. AddAuthenticationServices(config)   ← JWT + role policies
11. AddAppHealthChecks(config)          ← Postgres + Redis health checks
12. AddA2AAgents() (A2A açıksa)         ← Keyed ajanlar + A2A hosting + principal izolasyonu
```

### Neden bu sıra?

| Aşama | Bağımlılığı |
|---|---|
| Telemetry | Hiçbiri — diğerlerini instrument eder |
| AI | Telemetry (TelemetryChatClient decorator) |
| Redis | — |
| Persistence | Redis |
| Application | Persistence + Redis (port'lar adapter'ları kullanır) |
| Auth | — (sonda olur ki UserService gibi port'lar mevcut olsun) |
| HealthChecks | Persistence + Redis (bağımlılıkları kontrol eder) |
| A2A | AI + Application + Auth; yalnızca `A2A:Enabled=true` iken kaydedilir |

---

## HITL guard log

```csharp
if (!app.Environment.IsDevelopment())
{
    var approvalOpts = app.Services.GetRequiredService<IOptions<ApprovalOptions>>().Value;
    if (!approvalOpts.Enabled)
    {
        startupLogger.LogCritical(
            "[HITL] ApprovalOptions.Enabled=false — yan etkili tool'lar (sipariş/şikayet) " +
            "onaysız çalışıyor. Production ortamında kasıtlı mı?");
    }
}
```

Production'da `Approval:Enabled = false` ise **kritik uyarı** — `order_placement` ve `complaint_registration` tool'ları onaysız çalışır.

---

## A2A startup guard'ları

`A2A:Enabled=true` olduğunda servis kaydı, token değişimi ve ajan endpoint'leri birlikte açılır.
Kanal kapalıysa bunların hiçbiri yayınlanmaz.

`A2A:PublicBaseUrl`, Development dışında zorunlu ve mutlak bir HTTPS adresi olmalıdır; aksi
halde uygulama başlangıçta durur. Development ortamında boş bırakılabilir, fakat AgentCard
adresleri göreli olacağı için bir uyarı loglanır.

---

## Middleware pipeline

Sıra çok önemli — ASP.NET Core her middleware'i sırayla execute eder.

```
1. UseExceptionHandler()         ← Domain → HTTP mapping (en başta!)
2. MapAppHealthChecks()          ← /health/live, /health/ready, /health
3. UseCors()
4. UseRateLimiter()
5. UseWebSockets()               ← Auth'tan önce — WS upgrade
6. MapOpenApi() (dev only)
7. UseHttpsRedirection()
8. UseA2ARejectionLogging()      ← A2A açıksa; auth 401/403 sonuçlarını da sarar
9. UseAuthentication()
10. UseAuthorization()
11. UseA2AProtocolGuards()       ← A2A açıksa; model binding'den önce gövde/protokol kontrolü
12. MapXxxEndpoints()            ← Endpoint mapping (en sonda)
```

### Neden ExceptionHandler en başta?

Sonraki herhangi bir middleware exception fırlatırsa burada yakalanır. `DomainException` türetenler otomatik HTTP'ye çevrilir.

### Neden UseWebSockets() auth'tan önce?

WebSocket upgrade `HTTP 101 Switching Protocols` cevabını verir. Token `?access_token=...` query param'dan alınır (AuthServicesExtensions `OnMessageReceived` event'i).

### A2A middleware sırası neden böyle?

`UseA2ARejectionLogging`, yetkilendirme kısa devrelerini de görebilmek için authentication ve
authorization'ı dışarıdan sarar. `UseA2AProtocolGuards` ise yetkisiz gövdeleri okumamak için
authorization'dan sonra, A2A Minimal API model binding çalışmadan önce yer alır.

---

## Endpoint gruplandırma

```csharp
// Public — rate-limited, anonim veya JWT opsiyonel
app.MapAuthEndpoints();
if (a2aEnabled)
{
    app.MapA2AAuthEndpoints();
    app.MapA2AAgentEndpoints();
}
app.MapChatEndpoints();
app.MapRealtimeEndpoints();
app.MapSessionEndpoints();
app.MapAnalyticsEndpoints();    // /sessions/{sid}/rating public; /analytics/dashboard Admin

// Admin scope (JWT + role=Admin)
var adminScope = app.MapGroup("")
    .RequireAuthorization("Admin")
    .RequireRateLimiting("general");
adminScope.MapAdminEndpoints();
adminScope.MapTraceEndpoints();
adminScope.MapEvaluationEndpoints();
adminScope.MapMemoryEndpoints();
adminScope.MapImprovementsEndpoints();
adminScope.MapTelemetryEndpoints();
adminScope.MapPersonalizationEndpoints();
adminScope.MapAgentsEndpoints();
adminScope.MapSlaEndpoints();

// AdminOrAgent scope
var agentScope = app.MapGroup("")
    .RequireAuthorization("AdminOrAgent")
    .RequireRateLimiting("general");
agentScope.MapAgentPanelEndpoints();   // /agent/*
```

Her endpoint dosyasının kendi `MapXxxEndpoints` extension method'u vardır.

### Analytics neden ayrı?

`AnalyticsEndpoints` içinde hem Admin hem Anonymous endpoint'ler var:
- `GET /analytics/dashboard` → `.RequireAuthorization("Admin")`
- `POST /sessions/{sid}/rating` → `.RequireRateLimiting("general")` (anonim)

Bu yüzden admin group içine alınmayıp doğrudan `app.MapAnalyticsEndpoints()` ile ekleniyor; her endpoint kendi auth kuralını kendi tanımlıyor.

---

## Migration (dev only)

```csharp
await app.MigrateIfDevelopmentAsync();
```

`WebApplicationExtensions.MigrateIfDevelopmentAsync`:
- `env.IsDevelopment()` ve `Persistence:Provider = Postgres` ise
- `IDbContextFactory<CustomerSupportDbContext>` ile `MigrateAsync()` çağırır

Production'da migration **ayrı deploy adımı** olmalı.

---

## OpenAPI (dev only)

```csharp
if (app.Environment.IsDevelopment())
    app.MapOpenApi();
```

Development'ta `/openapi/v1.json` — Swagger UI / Scalar bağlanabilir. Production'da kapalı.

---

## Bağlantılar

- [Extensions.md](Extensions.md) — her `AddXxxServices` ne yapar
- [Infrastructure.md](Infrastructure.md) — DomainExceptionHandler detayı
- [Adapters.AI DependencyInjection](../CustomerSupportBot.Adapters.AI/DependencyInjection.md) — IChatClient kayıt deseni

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
```

### Neden bu sıra?

| Aşama | Bağımlılığı |
|---|---|
| Telemetry | Hiçbiri — diğerlerini instrument eder |
| AI | Telemetry (TelemetryChatClient decorator) |
| Redis | — |
| Persistence | Redis (PostgresAdapters distributed lock kullanır) |
| Application | Persistence + Redis (port'lar adapter'ları kullanır) |
| Auth | — (sonda olur ki UserService gibi port'lar mevcut olsun) |

---

## HITL guard log

```csharp
var approvalEnabled = config.GetValue<bool>("Approval:Enabled", true);
if (!approvalEnabled && !env.IsDevelopment())
{
    logger.LogCritical(
        "[HITL] Approval gate is DISABLED in non-development environment. " +
        "High-risk tools will execute without human approval.");
}
```

Production'da `Approval:Enabled = false` ise **kritik uyarı** — `order_placement` ve `complaint_registration` tool'ları onaysız çalışır. Bu config kazara unutulursa loglardan görülür.

---

## Middleware pipeline

Sıra çok önemli — ASP.NET Core her middleware'i sırayla execute eder.

```
1. UseExceptionHandler()         ← Domain → HTTP mapping (en başta!)
2. MapAppHealthChecks()          ← /health/live, /health/ready, /health
3. UseCors()
4. UseRateLimiter()
5. UseWebSockets()               ← Auth'tan önce — WS upgrade Authorization header taşıyabilsin
6. MapOpenApi() (dev only)
7. UseHttpsRedirection()
8. UseAuthentication()
9. UseAuthorization()
10. MapXxxEndpoints()            ← Endpoint mapping (en sonda)
```

### Neden ExceptionHandler en başta?

Sonraki herhangi bir middleware exception fırlatırsa burada yakalanır. Endpoint kodunda `try/catch` yazmaya gerek yok — `DomainException` türetenler otomatik HTTP'ye çevrilir.

### Neden UseWebSockets() auth'tan önce?

WebSocket upgrade `HTTP 101 Switching Protocols` cevabını verir. Bu cevap üretilmeden auth çalışırsa, `Authorization: Bearer ...` header'ı `ws://` URL'den taşınması zor. Bu yüzden:
- `UseWebSockets()` upgrade'i hazırlar
- Endpoint handler içinde `?access_token=...` query param'dan JWT alınır
- AuthServicesExtensions `OnMessageReceived` event'i bunu okur

---

## Endpoint gruplandırma

```csharp
// Public — anonim
app.MapAuthEndpoints();
app.MapChatEndpoints();
app.MapRealtimeEndpoints();
app.MapSessionEndpoints();
app.MapAnalyticsEndpoints();    // /sessions/{sid}/rating public

// Admin scope (JWT + role=Admin)
app.MapAdminEndpoints();
app.MapTraceEndpoints();
app.MapEvaluationEndpoints();
app.MapMemoryEndpoints();
app.MapImprovementsEndpoints();
app.MapTelemetryEndpoints();
app.MapPersonalizationEndpoints();
app.MapAgentsEndpoints();
app.MapWorkflowEndpoints();
app.MapSlaEndpoints();

// AdminOrAgent scope
app.MapAgentPanelEndpoints();   // /agent/*
```

Endpoint dosyalarının her biri kendi `MapXxxEndpoints` extension method'unu içerir.

---

## OpenAPI (dev only)

```csharp
if (env.IsDevelopment())
{
    app.MapOpenApi();
}
```

Development'ta `/openapi/v1.json` ile Swagger UI / Scalar bağlanabilir. Production'da kapalı — endpoint listesi sızdırılmaz.

---

## Migration (dev only)

```csharp
await app.MigrateIfDevelopmentAsync();
```

`WebApplicationExtensions.MigrateIfDevelopmentAsync`:
- `Persistence:Provider = Postgres` ise
- `env.IsDevelopment()` ise
- `DbContext.Database.MigrateAsync()` çağırır

Production'da migration **ayrı deploy adımı** olmalı — uygulama her start'ta migration yürütmesi tehlikeli (lock, downtime, sıralı deploy).

---

## Kullanım sırası

```csharp
var builder = WebApplication.CreateBuilder(args);

// DI registration (8 katman)
builder.Services.AddOpenApi();
builder.Services.AddLogging();
// ...
builder.Services.AddAuthenticationServices(builder.Configuration);

var app = builder.Build();

// Migration (dev only)
await app.MigrateIfDevelopmentAsync();

// Middleware pipeline
app.UseExceptionHandler();
app.MapAppHealthChecks();
// ...
app.UseAuthorization();

// Endpoint mapping
app.MapAuthEndpoints();
// ...

app.Run();
```

---

## Bağlantılar

- [Extensions.md](Extensions.md) — her `AddXxxServices` ne yapar
- [Infrastructure.md](Infrastructure.md) — DomainExceptionHandler detayı
- [Adapters.AI DependencyInjection](../adapters-ai/DependencyInjection.md) — IChatClient kayıt deseni

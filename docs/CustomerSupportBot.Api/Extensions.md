# Extensions — DI Modules

**Klasör:** `Extensions/`

DI kayıtlarının modüler dağılımı. `Program.cs` her birini çağırır.

| Extension | Sorumluluk |
|---|---|
| `AddTelemetryServices` | OpenTelemetry pipeline |
| `AddAiServices` | IChatClient + telemetry wrap + IGeneralChatClient + IReasoningChatClient |
| `AddRedisServices` | Distributed lock + message bus adapter'ları |
| `AddPersistenceServices` | EF Core / InMemory provider |
| `AddApplicationServices` | Port servisleri + CORS + rate limit + hosted services |
| `AddAuthenticationServices` | JWT + policies |
| `AddAppHealthChecks` / `MapAppHealthChecks` | Health endpoints |
| `MigrateIfDevelopmentAsync` | Dev'de EF Core migration |

---

## TelemetryExtensions

```csharp
public static IServiceCollection AddTelemetryServices(
    this IServiceCollection services,
    IConfiguration configuration)
    => services.AddTelemetryAdapters(
        configuration,
        activitySourceName: CustomerSupportTelemetry.ActivitySourceName,
        meterName: CustomerSupportTelemetry.MeterName);
```

`Adapters.Telemetry.AddTelemetryAdapters` ince bir wrapper. ActivitySource/Meter isimleri `CustomerSupportTelemetry` static class'ından alınır.

---

## AiServicesExtensions

**En önemli compose adımı:** `IChatClient` burada telemetry decorator ile sarılıyor.

```csharp
public static IServiceCollection AddAiServices(
    this IServiceCollection services,
    IConfiguration configuration)
{
    services.AddAiAdapters(configuration);   // AI adapter'ları kayıt

    // Standart IChatClient + telemetry wrap
    services.AddSingleton<IChatClient>(sp =>
    {
        var options = sp.GetRequiredService<IOptions<AiOptions>>().Value;
        var inner = AiClientFactory.CreateStandardChatClient(options);
        return WrapWithTelemetry(sp, inner, ResolveStandardModel(options), options.Provider.ToString());
    });

    // ReasoningChatClient (ayrı model/config)
    services.AddSingleton<ReasoningChatClient>(sp =>
    {
        var options = sp.GetRequiredService<IOptions<AiOptions>>().Value;
        return AiClientFactory.CreateReasoningChatClient(options, inner =>
            WrapWithTelemetry(sp, inner, ResolveReasoningModel(options), options.Provider.ToString()));
    });
    services.AddSingleton<IReasoningChatClient>(sp =>
        sp.GetRequiredService<ReasoningChatClient>());

    // IGeneralChatClient: IChatClient'ı adapte eder
    services.AddSingleton<IGeneralChatClient>(sp =>
        new GeneralChatClientAdapter(sp.GetRequiredService<IChatClient>()));

    return services;
}
```

### Telemetry wrap

```csharp
private static IChatClient WrapWithTelemetry(
    IServiceProvider sp, IChatClient inner, string modelHint, string provider)
{
    var telemetryOptions = sp.GetRequiredService<IOptions<TelemetryOptions>>().Value;
    if (!telemetryOptions.Enabled) return inner;

    return new TelemetryChatClient(
        inner,
        sp.GetRequiredService<ICostCalculatorPort>(),
        sp.GetRequiredService<CostUsageStore>(),
        modelHint, provider,
        sp.GetRequiredService<ILogger<TelemetryChatClient>>(),
        sp.GetService<ILlmCallPersistencePort>());
}
```

### Model hint resolution

Provider'a göre default model adı:

```
AiProvider.OpenAI      → options.OpenAI.Model
AiProvider.AzureOpenAI → options.AzureOpenAI.Deployment
```

Reasoning model için `ReasoningDeployment` / `ReasoningModel` öncelikli, yoksa standart model fallback.

---

## RedisServicesExtensions

```csharp
public static IServiceCollection AddRedisServices(
    this IServiceCollection services,
    IConfiguration configuration)
    => services.AddRedisAdapters(configuration);
```

İnce wrapper — `Adapters.Redis.AddRedisAdapters` çağırır. `IAppDistributedLock`, `IMessageBusPort` kayıt edilir.

---

## PersistenceServicesExtensions

```csharp
public static IServiceCollection AddPersistenceServices(
    this IServiceCollection services,
    IConfiguration configuration)
    => services.AddPersistenceAdapters(configuration);
```

İnce wrapper. Provider seçimi (InMemory/Postgres) `Adapters.Persistence` içinde yapılır.

---

## ApplicationServicesExtensions

Bu en kalın extension — birkaç sorumluluk birleştirir:

### 1. Application port'ları

```csharp
services.AddApplicationDrivingPorts(configuration);
```

Application katmanındaki `IApprovalPort`, `IChatSessionPort`, `IAnalyticsPort`, vb. driving port'ları kaydeder.

### 2. CORS

```csharp
var allowedOrigins = configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>();

services.AddCors(options =>
    options.AddDefaultPolicy(p =>
    {
        if (allowedOrigins is { Length: > 0 })
            p.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader();
        else
            p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();    // ⚠️ Dev mode
    }));
```

`Cors:AllowedOrigins` boşsa `AllowAnyOrigin` — sadece dev. Production'da whitelist olmalı.

### 3. JSON serialization

```csharp
services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));
```

Enum'lar camelCase string olarak serialize edilir: `IssueSeverity.Warn` → `"warn"`.

### 4. Rate limiting

```csharp
services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("chat", ...);      // 20/dakika/IP
    options.AddPolicy("general", ...);  // 60/dakika/IP
});
```

| Policy | Limit | Kullanan endpoint'ler |
|---|---|---|
| `chat` | 20/dakika/IP | `/chat/`, `/chat/stream` |
| `general` | 60/dakika/IP | `/sessions/.../rating` (anon) |

`QueueLimit = 0` — kuyruk yok, aşınca hemen 429.

### 5. Agent ekibi + ChatEventOrchestrator

```csharp
services.AddAgentsAdapter();                         // CustomerSupportTeam, ApprovalGateService
services.AddScoped<ChatEventOrchestrator>();
```

### 6. Hosted services

```csharp
services.AddHostedService<SlaGuardianService>();
services.AddHostedService<KnowledgeBaseStartupService>();
```

---

## AuthServicesExtensions

### JWT setup

```csharp
services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;   // Development için
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        // Query string token desteği (SSE / WebSocket)
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"].ToString();
                if (!string.IsNullOrEmpty(token)) ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });
```

`SigningKey` boşsa `new string('x', 32)` placeholder — `JwtAccessTokenProvider` boot sırasında throw eder.

### Neden query string token?

| Senaryo | Header yöntemi | Query string |
|---|---|---|
| Normal fetch/XHR | ✅ `Authorization: Bearer` | — |
| EventSource (SSE) | ❌ Custom header eklenemez | ✅ `?access_token=...` |
| WebSocket | ❌ Browser API header'ı limitli | ✅ `?access_token=...` |

Query string güvenliği: HTTPS şart; log'larda `?access_token=` mask edilmeli.

### Policies

```csharp
services.AddAuthorization(options =>
{
    options.AddPolicy("Admin",        p => p.RequireRole("Admin"));
    options.AddPolicy("Agent",        p => p.RequireRole("Agent"));
    options.AddPolicy("AdminOrAgent", p => p.RequireRole("Admin", "Agent"));
});
```

### Auth port'ları

```csharp
services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
services.AddScoped<IJwtAccessTokenProvider, JwtAccessTokenProvider>();
services.AddScoped<ITokenService, TokenPortService>();
services.AddScoped<IUserService, Application.Services.Auth.UserService>();
```

---

## HealthCheckExtensions

### `AddAppHealthChecks`

```csharp
if (persistence.Provider == PersistenceProvider.Postgres)
{
    builder.Add(new HealthCheckRegistration(
        "postgresql",
        sp => new PostgresHealthCheck(sp.GetRequiredService<IDbContextFactory<CustomerSupportDbContext>>()),
        HealthStatus.Unhealthy,
        ["db", "ready"]));
}

builder.Add(new HealthCheckRegistration(
    "redis",
    sp => new RedisHealthCheck(sp.GetRequiredService<IConnectionMultiplexer>()),
    HealthStatus.Unhealthy,
    ["cache", "ready"]));
```

### `MapAppHealthChecks`

```csharp
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }))
   .AllowAnonymous();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = hc => hc.Tags.Contains("ready"),
    // ...
}).AllowAnonymous();

app.MapHealthChecks("/health", ...).AllowAnonymous();
```

### Endpoint'ler

| Endpoint | Davranış | Kubernetes |
|---|---|---|
| `/health/live` | Sadece process açık mı? | liveness probe |
| `/health/ready` | DB + Redis ulaşılabilir mi? | readiness probe |
| `/health` | Tüm check'ler | Monitoring |

JSON response: `{ status, totalMs, checks: [{name, status, description, durationMs, error}] }`

---

## WebApplicationExtensions

### `MigrateIfDevelopmentAsync`

```csharp
public static async Task MigrateIfDevelopmentAsync(this WebApplication app)
{
    if (!app.Environment.IsDevelopment()) return;
    var opts = app.Services.GetRequiredService<IOptions<PersistenceOptions>>().Value;
    if (opts.Provider != PersistenceProvider.Postgres) return;

    await using var scope = app.Services.CreateAsyncScope();
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CustomerSupportDbContext>>();
    await using var ctx = await factory.CreateDbContextAsync();
    await ctx.Database.MigrateAsync();
}
```

Sadece **development + Postgres** kombinasyonunda migration uygula. Production deploy'unda CI/CD pipeline ayrı adımda `dotnet ef database update` çalıştırır.

---

## Bağlantılar

- [Program.md](Program.md) — `Add*Services` çağrı sırası
- [Adapters.AI DependencyInjection](../adapters-ai/DependencyInjection.md) — Composition Root rationale
- [Adapters.Persistence DependencyInjection](../adapters-persistence/DependencyInjection.md)

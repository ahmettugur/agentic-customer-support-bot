# Extensions — DI Modules

**Klasör:** `Extensions/`

DI kayıtlarının modüler dağılımı. `Program.cs` her birini çağırır.

| Extension | Sorumluluk |
|---|---|
| `AddTelemetryServices` | OpenTelemetry pipeline |
| `AddAiServices` | IChatClient + telemetry wrap + IGeneralChatClient + IReasoningChatClient |
| `AddRedisServices` | Distributed lock + message bus |
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
{
    return services.AddTelemetryAdapters(
        configuration,
        activitySourceName: TelemetryConstants.ActivitySourceName,
        meterName: TelemetryConstants.MeterName);
}
```

`Adapters.Telemetry.AddTelemetryAdapters` ince bir wrapper. ActivitySource/Meter isimleri `TelemetryConstants` static class'ından (uygulamadaki tüm telemetry kodu bunu kullanır).

---

## AiServicesExtensions

**En önemli compose adımı:** `IChatClient` burada telemetry decorator ile sarılıyor.

```csharp
public static IServiceCollection AddAiServices(
    this IServiceCollection services,
    IConfiguration configuration)
{
    services.AddAiAdapters(configuration);   // Embedding, Vector, Realtime kayıt

    // IChatClient: factory + telemetry wrap
    services.AddSingleton<IChatClient>(sp =>
    {
        var aiOptions = sp.GetRequiredService<IOptions<AiOptions>>().Value;
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        IChatClient inner = AiClientFactory.CreateStandardChatClient(aiOptions, loggerFactory);

        var telemetryEnabled = configuration.GetValue<bool>("Telemetry:Enabled", true);
        if (!telemetryEnabled) return inner;

        return new TelemetryChatClient(
            inner,
            sp.GetRequiredService<ICostCalculatorPort>(),
            sp.GetRequiredService<CostUsageStore>(),
            modelHint: ResolveModelHint(aiOptions),
            provider: aiOptions.Provider.ToString().ToLowerInvariant(),
            sp.GetRequiredService<ILogger<TelemetryChatClient>>(),
            sp.GetService<ILlmCallPersistencePort>());
    });

    // ReasoningChatClient: aynı pattern
    services.AddSingleton<IReasoningChatClient>(sp => /* ... */);

    // IGeneralChatClient: IChatClient'ı adapte eder
    services.AddSingleton<IGeneralChatClient>(sp =>
        new GeneralChatClientAdapter(sp.GetRequiredService<IChatClient>()));

    return services;
}
```

### Model hint resolution

`ResolveModelHint(aiOptions)` provider'a göre default modeli döner:

```csharp
AiProvider.OpenAI      → aiOptions.OpenAI.Model
AiProvider.AzureOpenAI → aiOptions.AzureOpenAI.Deployment
AiProvider.Anthropic   → aiOptions.Anthropic.Model
```

Telemetry bu hint'i `ai.model` tag'i olarak kullanır.

---

## RedisServicesExtensions

```csharp
public static IServiceCollection AddRedisServices(
    this IServiceCollection services,
    IConfiguration configuration)
{
    return services.AddRedisAdapters(configuration);
}
```

İnce wrapper — sadece `Adapters.Redis`'i çağırır. Burada `IAppDistributedLock`, `IMessageBusPort` kayıt edilir.

---

## PersistenceServicesExtensions

```csharp
public static IServiceCollection AddPersistenceServices(
    this IServiceCollection services,
    IConfiguration configuration)
{
    return services.AddPersistenceAdapters(configuration);
}
```

Aynı şekilde ince wrapper. Provider seçimi (InMemory/Postgres) `Adapters.Persistence` içinde yapılır.

---

## ApplicationServicesExtensions

Bu en kalın extension — birkaç sorumluluk birleştirir:

### 1. Application port'ları

```csharp
services.AddApplicationDrivingPorts();
```

Application katmanındaki `IApprovalPort`, `IChatSessionPort`, `IAnalyticsPort`, vb. driving port'ları kaydeder.

### 2. CORS

```csharp
var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
services.AddCors(options =>
{
    options.AddDefaultPolicy(builder =>
    {
        if (allowedOrigins.Length > 0)
            builder.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
        else
            builder.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();    // ⚠️ Dev mode
    });
});
```

`Cors:AllowedOrigins` boşsa `AllowAnyOrigin` — sadece dev. Production'da whitelist olmalı.

### 3. JSON serialization

```csharp
services.ConfigureHttpJsonOptions(opts =>
{
    opts.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    opts.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});
```

Enum'lar camelCase string olarak serialize edilir: `IssueSeverity.Warn` → `"warn"`.

### 4. Rate limiting

```csharp
services.AddRateLimiter(opts =>
{
    opts.AddFixedWindowLimiter("chat", o =>
    {
        o.PermitLimit = 20;
        o.Window = TimeSpan.FromMinutes(1);
        o.PartitionKey = httpCtx => httpCtx.Connection.RemoteIpAddress?.ToString();
    });
    opts.AddFixedWindowLimiter("general", o =>
    {
        o.PermitLimit = 60;
        o.Window = TimeSpan.FromMinutes(1);
    });
});
```

| Policy | Limit | Kullanan endpoint'ler |
|---|---|---|
| `chat` | 20/dakika/IP | `/chat`, `/chat/stream` |
| `general` | 60/dakika/IP | `/sessions/.../rating` (anon) |

429 Too Many Requests aşılınca döner.

### 5. Agent ekibi + ChatEventOrchestrator

```csharp
services.AddAgentsAdapters();    // CustomerSupportTeam, ApprovalGateService
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
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        // Query string token desteği
        opts.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"].FirstOrDefault();
                if (!string.IsNullOrEmpty(token))
                    context.Token = token;
                return Task.CompletedTask;
            }
        };
    });
```

### Neden query string token?

| Senaryo | Header yöntemi | Query string |
|---|---|---|
| Normal fetch/XHR | ✅ `Authorization: Bearer` | — |
| EventSource (SSE) | ❌ Custom header eklenemez | ✅ `?access_token=...` |
| WebSocket | ❌ Browser API header'ı limitli | ✅ `?access_token=...` |

Query string güvenliği:
- HTTPS şart (URL şifreli)
- Log'larda görünür (production'da `?access_token=` filter'ı log'da uygula)
- Refresh token değil — sadece kısa ömürlü access token

### Policies

```csharp
services.AddAuthorization(opts =>
{
    opts.AddPolicy("Admin",         p => p.RequireRole("Admin"));
    opts.AddPolicy("Agent",         p => p.RequireRole("Agent"));
    opts.AddPolicy("AdminOrAgent",  p => p.RequireRole("Admin", "Agent"));
});
```

Endpoint mapping'de `.RequireAuthorization("Admin")` ile kullanılır.

### Auth port'ları

```csharp
services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
services.AddScoped<IJwtAccessTokenProvider, JwtAccessTokenProvider>();
services.AddScoped<ITokenService, TokenPortService>();
services.AddScoped<IUserService, UserService>();
```

---

## HealthCheckExtensions

```csharp
public static IServiceCollection AddAppHealthChecks(
    this IServiceCollection services,
    IConfiguration configuration)
{
    var hcBuilder = services.AddHealthChecks();

    if (configuration["Persistence:Provider"] == "Postgres")
    {
        hcBuilder.AddCheck<PostgresHealthCheck>("postgres", tags: new[] { "db", "ready" });
    }

    hcBuilder.AddCheck<RedisHealthCheck>("redis", tags: new[] { "cache", "ready" });

    return services;
}

public static WebApplication MapAppHealthChecks(this WebApplication app)
{
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false   // Sadece process alive check, downstream check yok
    });

    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = c => c.Tags.Contains("ready")
    });

    app.MapHealthChecks("/health");   // Tüm check'ler

    return app;
}
```

### Endpoint'ler

| Endpoint | Davranış | Kubernetes |
|---|---|---|
| `/health/live` | Sadece process açık mı? | liveness probe |
| `/health/ready` | DB + Redis ulaşılabilir mi? | readiness probe |
| `/health` | Tüm check'ler | Monitoring |

### Tag ayrımı

| Tag | Anlamı |
|---|---|
| `ready` | Trafik almaya hazır olmak için bu check geçmeli |
| `db` | Veritabanı bağımlılığı |
| `cache` | Redis bağımlılığı |

---

## WebApplicationExtensions

### `MigrateIfDevelopmentAsync`

```csharp
public static async Task MigrateIfDevelopmentAsync(this WebApplication app)
{
    if (!app.Environment.IsDevelopment()) return;
    if (app.Configuration["Persistence:Provider"] != "Postgres") return;

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CustomerSupportDbContext>();
    await db.Database.MigrateAsync();
    app.Logger.LogInformation("[Migration] Applied EF Core migrations (dev mode)");
}
```

Sadece **development + Postgres** kombinasyonunda migration uygula. Production deploy'unda CI/CD pipeline ayrı bir adımda `dotnet ef database update` çalıştırır.

---

## Bağlantılar

- [Program.md](Program.md) — `Add*Services` çağrı sırası
- [Adapters.AI DependencyInjection](../adapters-ai/DependencyInjection.md) — Composition Root rationale
- [Adapters.Persistence DependencyInjection](../adapters-persistence/DependencyInjection.md)

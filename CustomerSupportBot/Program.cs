using System.ClientModel;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Azure.AI.OpenAI;
using CustomerSupportBot.Agents;
using CustomerSupportBot.Endpoints;
using CustomerSupportBot.Evaluation;
using CustomerSupportBot.Infrastructure.Persistence;
using CustomerSupportBot.Models;
using CustomerSupportBot.Models.Auth;
using CustomerSupportBot.Services;
using CustomerSupportBot.Services.Auth;
using CustomerSupportBot.Services.Providers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenAI;

var builder = WebApplication.CreateBuilder(args);

// ─── SERVICES ───
builder.Services.AddOpenApi();
builder.Services.AddLogging();

// Tüm minimal API yanıtlarında enum'ları camelCase string olarak serialize et
// (ör. IssueSeverity.Warn → "warn"). Frontend bu formatı karşılaştırır.
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

// AI sağlayıcı yapılandırması — `AI` bölümünden bind edilir.
// Sağlayıcı (OpenAI / AzureOpenAI / Anthropic) AiOptions.Provider üzerinden seçilir.
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));

// ─── PERSISTENCE ───
// Persistence:Provider = "InMemory" (default) | "Postgres"
// Postgres seçilirse EF Core DbContextFactory kaydedilir; ileride store
// implementasyonları bu factory üzerinden DbContext açar.
var persistenceSection = builder.Configuration.GetSection(PersistenceOptions.SectionName);
builder.Services.Configure<PersistenceOptions>(persistenceSection);
var persistenceOptions = persistenceSection.Get<PersistenceOptions>() ?? new PersistenceOptions();

if (persistenceOptions.Provider == PersistenceProvider.Postgres)
{
    var pgConnection = builder.Configuration.GetConnectionString("PostgreSQL")
        ?? throw new InvalidOperationException(
            "Persistence:Provider=Postgres ancak ConnectionStrings:PostgreSQL tanımlı değil.");
    builder.Services.AddCustomerSupportPersistence(pgConnection);
    builder.Services.AddHostedService<PersistenceHydrator>();
}

builder.Services.AddSingleton<IChatClient>(sp =>
{
    var options = sp.GetRequiredService<IOptions<AiOptions>>().Value;
    return AiClientFactory.CreateStandardChatClient(options);
});

builder.Services.AddSingleton<ReasoningChatClient>(sp =>
{
    var options = sp.GetRequiredService<IOptions<AiOptions>>().Value;
    return AiClientFactory.CreateReasoningChatClient(options);
});

// Prompt yükleyici — Prompts/**/*.md dosyalarını belleğe alır
builder.Services.AddSingleton<PromptService>();

// HITL — Approval gate service (tool wrapper + escalation detection)
builder.Services.AddSingleton<ApprovalGateService>();

// HITL — Approval context accessor (AsyncLocal-tabanlı, IDisposable scope ile temizlik)
builder.Services.AddSingleton<IApprovalContextAccessor, ApprovalContextAccessor>();

// Domain servisleri
builder.Services.AddSingleton<EntityVerifier>();           // ReAct-lite entity grounding
builder.Services.AddSingleton<ReasoningSanityChecker>();   // Deterministic tutarsızlık tarama
builder.Services.AddSingleton<ReasoningService>();
builder.Services.AddSingleton<RevisionService>();
builder.Services.AddSingleton<CustomerSupportTeam>();
builder.Services.AddSingleton<ICustomerSupportTeam>(sp => sp.GetRequiredService<CustomerSupportTeam>());
builder.Services.AddSingleton<EvaluationRunner>();

// Chat orchestrators — istek başına yeni instance (Scoped lifecycle)
// Orchestrator'lar yalnızca endpoint handler'larında resolve edilir,
// dolayısıyla captive dependency riski yoktur.
builder.Services.AddScoped<ChatStreamOrchestrator>();
builder.Services.AddScoped<ChatEventOrchestrator>();

// Reasoning trace store (in-memory veya Postgres write-through)
if (persistenceOptions.Provider == PersistenceProvider.Postgres)
{
    builder.Services.AddSingleton<IReasoningTraceStore, CustomerSupportBot.Services.Persistence.PostgresReasoningTraceStore>();
}
else
{
    builder.Services.AddSingleton<IReasoningTraceStore, InMemoryReasoningTraceStore>();
}

// ─── HITL — Human-in-the-Loop ───
// Approval queue + Escalation sink + config (appsettings.json > HumanInTheLoop).
builder.Services.Configure<ApprovalOptions>(
    builder.Configuration.GetSection("HumanInTheLoop"));
if (persistenceOptions.Provider == PersistenceProvider.Postgres)
{
    builder.Services.AddSingleton<IApprovalQueue, CustomerSupportBot.Services.Persistence.PostgresApprovalQueue>();
    builder.Services.AddSingleton<IEscalationSink, CustomerSupportBot.Services.Persistence.PostgresEscalationSink>();
}
else
{
    builder.Services.AddSingleton<IApprovalQueue, InMemoryApprovalQueue>();
    builder.Services.AddSingleton<IEscalationSink, InMemoryEscalationSink>();
}

// HITL Live Takeover — session başına Bot/Human kipi + user<->admin mesaj köprüsü
if (persistenceOptions.Provider == PersistenceProvider.Postgres)
{
    builder.Services.AddSingleton<IChatModeRegistry, CustomerSupportBot.Services.Persistence.PostgresChatModeRegistry>();
}
else
{
    builder.Services.AddSingleton<IChatModeRegistry, InMemoryChatModeRegistry>();
}
if (persistenceOptions.Provider == PersistenceProvider.Postgres)
{
    builder.Services.AddSingleton<IChatBridge, CustomerSupportBot.Services.Persistence.PostgresChatBridge>();
}
else
{
    builder.Services.AddSingleton<IChatBridge, InMemoryChatBridge>();
}

// Oturum yöneticisi (hem ISessionManager hem IConversationStore)
if (persistenceOptions.Provider == PersistenceProvider.Postgres)
{
    builder.Services.AddSingleton<CustomerSupportBot.Services.Persistence.PostgresSessionManager>();
    builder.Services.AddSingleton<ISessionManager>(sp => sp.GetRequiredService<CustomerSupportBot.Services.Persistence.PostgresSessionManager>());
    builder.Services.AddSingleton<IConversationStore>(sp => sp.GetRequiredService<CustomerSupportBot.Services.Persistence.PostgresSessionManager>());
}
else
{
    builder.Services.AddSingleton<InMemorySessionManager>();
    builder.Services.AddSingleton<ISessionManager>(sp => sp.GetRequiredService<InMemorySessionManager>());
    builder.Services.AddSingleton<IConversationStore>(sp => sp.GetRequiredService<InMemorySessionManager>());
}

// Konuşma değerlendirme deposu + Analytics servisi
// Persistence provider seçimi: Postgres ⇒ DB write-through, InMemory ⇒ eski davranış.
if (persistenceOptions.Provider == PersistenceProvider.Postgres)
{
    builder.Services.AddSingleton<IRatingStore, CustomerSupportBot.Services.Persistence.PostgresRatingStore>();
}
else
{
    builder.Services.AddSingleton<IRatingStore, InMemoryRatingStore>();
}
builder.Services.AddSingleton<AnalyticsService>();

// Context provider'lar
builder.Services.AddSingleton<IContextProvider, CustomerContextProvider>();
builder.Services.AddSingleton<IContextProvider, ConversationSummaryProvider>();
builder.Services.AddSingleton<ContextPipeline>();

// Güvenlik — deterministik input gate (LLM'e ulaşmadan önce)
builder.Services.AddSingleton<InputGuard>();

// Güvenlik — rate limiting (per-IP token bucket)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Chat endpoint'leri: dakikada 20 istek per IP, kuyruk yok
    options.AddPolicy("chat", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

// ─── AUTHENTICATION & AUTHORIZATION ───
// JWT bearer + role-based authorization. Admin endpoint'leri "Admin" rolü ister.
// SSE endpoint'leri Bearer header taşıyamadığı için ?access_token=... query
// param'ı destekleniyor (OnMessageReceived).
var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
builder.Services.Configure<JwtOptions>(jwtSection);
var jwtOptions = jwtSection.Get<JwtOptions>() ?? new JwtOptions();

builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddSingleton<IUserService, UserService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false; // Development için
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                string.IsNullOrWhiteSpace(jwtOptions.SigningKey)
                    ? new string('x', 32) // boşsa boot fail edecek (TokenService throw eder)
                    : jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        // SSE / EventSource için query string desteği
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

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", p => p.RequireRole("Admin"));
});

// ─── PIPELINE ───
var app = builder.Build();

// Development'ta Postgres seçildiyse şemayı otomatik migrate et.
// Production'da migration ayrı bir deploy adımı olarak çalıştırılır.
if (app.Environment.IsDevelopment()
    && persistenceOptions.Provider == PersistenceProvider.Postgres)
{
    await using var scope = app.Services.CreateAsyncScope();
    var factory = scope.ServiceProvider
        .GetRequiredService<IDbContextFactory<CustomerSupportDbContext>>();
    await using var ctx = await factory.CreateDbContextAsync();
    await ctx.Database.MigrateAsync();
}

app.UseCors();
app.UseRateLimiter();
app.UseDefaultFiles();   // / isteğinde index.html'i bul
app.UseStaticFiles();    // Wwwroot içeriğini sun

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

// ─── ENDPOINTS ───
app.MapAuthEndpoints();         // /auth/login, /auth/refresh, /auth/logout
app.MapChatEndpoints();         // user-facing chat (anonim)
app.MapSessionEndpoints();      // user-facing session listesi (anonim)

// Admin gerektiren endpoint grupları
var adminScope = app.MapGroup("").RequireAuthorization("Admin");
adminScope.MapAdminEndpoints();        // HITL — /approvals/* + /escalations/* + /chat-sessions/*
adminScope.MapTraceEndpoints();        // /traces/*
adminScope.MapEvaluationEndpoints();   // /eval/*

// Analytics: dashboard'lar admin, rating endpoint'leri kullanıcıya açık
app.MapAnalyticsEndpoints();

app.Run();

public partial class Program { }

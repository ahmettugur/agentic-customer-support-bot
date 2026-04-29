using System.ClientModel;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Azure.AI.OpenAI;
using CustomerSupportBot.Agents;
using CustomerSupportBot.Endpoints;
using CustomerSupportBot.Evaluation;
using CustomerSupportBot.Models;
using CustomerSupportBot.Services;
using CustomerSupportBot.Services.Providers;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
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
builder.Services.AddSingleton<EvaluationRunner>();

// Chat orchestrators — istek başına yeni instance (Scoped lifecycle)
// Orchestrator'lar yalnızca endpoint handler'larında resolve edilir,
// dolayısıyla captive dependency riski yoktur.
builder.Services.AddScoped<ChatStreamOrchestrator>();
builder.Services.AddScoped<ChatEventOrchestrator>();

// Reasoning trace store (in-memory, OTel yerine)
builder.Services.AddSingleton<IReasoningTraceStore, InMemoryReasoningTraceStore>();

// ─── HITL — Human-in-the-Loop ───
// Approval queue + Escalation sink + config (appsettings.json > HumanInTheLoop).
builder.Services.Configure<ApprovalOptions>(
    builder.Configuration.GetSection("HumanInTheLoop"));
builder.Services.AddSingleton<IApprovalQueue, InMemoryApprovalQueue>();
builder.Services.AddSingleton<IEscalationSink, InMemoryEscalationSink>();

// HITL Live Takeover — session başına Bot/Human kipi + user<->admin mesaj köprüsü
builder.Services.AddSingleton<IChatModeRegistry, InMemoryChatModeRegistry>();
builder.Services.AddSingleton<IChatBridge, InMemoryChatBridge>();

// Oturum yöneticisi (hem ISessionManager hem IConversationStore)
builder.Services.AddSingleton<InMemorySessionManager>();
builder.Services.AddSingleton<ISessionManager>(sp => sp.GetRequiredService<InMemorySessionManager>());
builder.Services.AddSingleton<IConversationStore>(sp => sp.GetRequiredService<InMemorySessionManager>());

// Konuşma değerlendirme deposu + Analytics servisi
builder.Services.AddSingleton<IRatingStore, InMemoryRatingStore>();
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

// ─── PIPELINE ───
var app = builder.Build();

app.UseCors();
app.UseRateLimiter();
app.UseDefaultFiles();   // / isteğinde index.html'i bul
app.UseStaticFiles();    // Wwwroot içeriğini sun

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// ─── ENDPOINTS ───
app.MapChatEndpoints();
app.MapSessionEndpoints();
app.MapTraceEndpoints();
app.MapEvaluationEndpoints();
app.MapAdminEndpoints();        // HITL — /approvals/* + /escalations/*
app.MapAnalyticsEndpoints();    // Analytics dashboard + ratings

app.Run();

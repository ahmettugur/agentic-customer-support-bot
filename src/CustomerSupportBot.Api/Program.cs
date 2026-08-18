using CustomerSupportBot.Api.Endpoints;
using CustomerSupportBot.Api.Extensions;
using CustomerSupportBot.Api.Infrastructure;
using CustomerSupportBot.Application.Ports.Outbound;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
});

builder.Services.AddOpenApi();
builder.Services.AddLogging();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddTelemetryServices(builder.Configuration);
builder.Services.AddAiServices(builder.Configuration);
builder.Services.AddRedisServices(builder.Configuration);
builder.Services.AddPersistenceServices(builder.Configuration);
builder.Services.AddApplicationServices(builder.Configuration);
builder.Services.AddAuthenticationServices(builder.Configuration);
builder.Services.AddAppHealthChecks(builder.Configuration);

// A2A (Agent2Agent) — dış sistemlere açılan kanal. Kapalıysa ajanlar hiç kaydedilmez ve
// endpoint hiç map edilmez: kapalı bir kanalın yayında olmaması, yetkiyle engellenmesinden
// daha güvenlidir (yanlış yapılandırma yüzeyi hiç doğmaz).
var a2aEnabled = builder.Configuration.GetValue<bool>("A2A:Enabled");
if (a2aEnabled)
{
    builder.Services.AddA2AAgents();
}

var app = builder.Build();

// HITL guard: production'da HITL devre dışıysa açık uyarı bas
if (!app.Environment.IsDevelopment())
{
    var approvalOpts = app.Services.GetRequiredService<IOptions<ApprovalOptions>>().Value;
    if (!approvalOpts.Enabled)
    {
        var startupLogger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Startup");
        startupLogger.LogCritical(
            "[HITL] ApprovalOptions.Enabled=false — yan etkili tool'lar (sipariş/şikayet) " +
            "onaysız çalışıyor. Production ortamında kasıtlı mı? appsettings'i kontrol edin.");
    }
}

await app.MigrateIfDevelopmentAsync();

// Inbound boundary — domain exception → HTTP status/ProblemDetails çevirisi
app.UseExceptionHandler();

app.MapAppHealthChecks();

app.UseCors();
app.UseRateLimiter();
app.UseWebSockets();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapA2AAuthEndpoints();
if (a2aEnabled)
{
    app.MapA2AAgentEndpoints();
}
app.MapChatEndpoints();
app.MapRealtimeEndpoints();
app.MapSessionEndpoints();

// Admin/agent uçları auth arkasında olduğu için önceden rate limit yoktu — bir
// kimlik bilgisi sızarsa (veya kötüye kullanılırsa) sınırsız istek atılabiliyordu.
// "general" (60/dk/IP) politikası zaten AnalyticsEndpoints'te kullanılıyor; burada
// da aynı eşiği uyguluyoruz.
var adminScope = app.MapGroup("").RequireAuthorization("Admin").RequireRateLimiting("general");
adminScope.MapAdminEndpoints();
adminScope.MapTraceEndpoints();
adminScope.MapEvaluationEndpoints();
adminScope.MapMemoryEndpoints();
adminScope.MapImprovementsEndpoints();
adminScope.MapTelemetryEndpoints();
adminScope.MapPersonalizationEndpoints();
adminScope.MapAgentsEndpoints();
adminScope.MapSlaEndpoints();

var agentScope = app.MapGroup("").RequireAuthorization("AdminOrAgent").RequireRateLimiting("general");
agentScope.MapAgentPanelEndpoints();

app.MapAnalyticsEndpoints();

app.Run();

namespace CustomerSupportBot.Api
{
    public partial class Program { }
}

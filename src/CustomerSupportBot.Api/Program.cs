using CustomerSupportBot.Api.Endpoints;
using CustomerSupportBot.Api.Extensions;
using CustomerSupportBot.Api.Infrastructure;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Services.A2A;
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

// A2A guard: kanal açıkken kart adresleri dış istemcilerin okuyacağı adreslerdir.
// PublicBaseUrl yalnızca Development'ta boş kalabilir; bu durumda kartlarda GÖRELİ URL'ler
// yayınlanır. Dış ortamda boşluk da göreli değer kadar hatalıdır ve startup'ı durdurur.
// Spesifikasyon üretim HTTP arayüzleri için mutlak HTTPS adres bekler.
if (a2aEnabled)
{
    var a2aOpts = app.Services.GetRequiredService<IOptions<A2AOptions>>().Value;
    var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

    if (string.IsNullOrWhiteSpace(a2aOpts.PublicBaseUrl) && !app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "A2A:PublicBaseUrl Development dışında zorunludur ve mutlak bir HTTPS adresi olmalıdır.");
    }
    else if (string.IsNullOrWhiteSpace(a2aOpts.PublicBaseUrl))
    {
        startupLogger.LogWarning(
            "[A2A] PublicBaseUrl boş — agent card'larda göreli adresler yayınlanacak. "
          + "Dışa açılan bir kurulumda mutlak HTTPS adresi verin (ör. https://api.ornek.com).");
    }
    else if (!Uri.TryCreate(a2aOpts.PublicBaseUrl, UriKind.Absolute, out var baseUri))
    {
        throw new InvalidOperationException(
            $"A2A:PublicBaseUrl mutlak bir adres olmalı: '{a2aOpts.PublicBaseUrl}'. "
          + "Göreli bir değer, kartlarda çözümlenemeyen adresler üretir.");
    }
    else if (baseUri.Scheme != Uri.UriSchemeHttps && !app.Environment.IsDevelopment())
    {
        // localhost üzerinde http ile çalışmak geliştiricinin işini kolaylaştırır; dışarıya
        // http bir kart yayınlamak ise token'ları düz metin taşıyan bir kanal ilan etmektir.
        throw new InvalidOperationException(
            $"A2A:PublicBaseUrl Development dışında HTTPS olmalı: '{a2aOpts.PublicBaseUrl}'.");
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
// A2A red logu, yetkilendirmeyi SARMALAMAK zorunda: UseAuthorization başarısız politikada
// kısa devre yapar ve kendisinden SONRA kayıtlı hiçbir middleware/endpoint filtresi çalışmaz.
// Ölçüldü — sonrasına konduğunda 401/403 istekleri loglarda hiç görünmüyordu.
if (a2aEnabled)
{
    app.UseA2ARejectionLogging();
}

app.UseAuthentication();
app.UseAuthorization();

// Endpoint metadata'sı routing sırasında hazırdır; guard yetkilendirmeden sonra, Minimal API
// model binding'inden önce çalışır. Böylece yetkisiz gövdeler boşuna okunmaz ve yetkili A2A
// gövdeleri deserialize edilmeden önce kesin olarak sınırlandırılır.
if (a2aEnabled)
{
    app.UseA2AProtocolGuards();
}

app.MapAuthEndpoints();
// Token değişimi de bayrağa BAĞLI: kapalıyken yalnızca ajan uçlarını kaldırmak, kanalı
// gerçekten kapatmaz. Daha önce oluşturulmuş bir Partner hesabı, kanal kapatıldıktan sonra
// da özne token'ı üretmeye devam edebilirdi — ajanlar 404 döndüğü için veri sızmaz ama
// "kanal tamamen kapalı" garantisi yanlış olur ve kapatma işlemi eksik kalır.
if (a2aEnabled)
{
    app.MapA2AAuthEndpoints();
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

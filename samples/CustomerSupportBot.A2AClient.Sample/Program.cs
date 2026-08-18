// A2A istemci ornegi — bir PARTNER sisteminin yazacagi entegrasyonun referansi.
//
// Bu proje bilerek bizim hicbir projemize referans VERMEZ; yalnizca herkese acik NuGet
// paketlerini kullanir. Boylece "disaridan bakan biri gercekten baglanabiliyor mu" sorusu
// durustce olculur — paylasilan tiplerle calissaydi entegrasyonu degil kendi ic
// tutarliligimizi test ederdik.
//
// Iki mod:
//   (varsayilan)  Etkilesimli ajan — siz yazarsiniz, YEREL bir ajan hangi uzak ajana
//                 gidecegine karar verir. Uzak A2A ajanlari tool olarak baglanir.
//   --verify      LLM'siz uctan uca dogrulama; CI icin cikis kodu dondurur.
//
// Kullanim:
//   dotnet run --project samples/CustomerSupportBot.A2AClient.Sample
//   dotnet run --project samples/CustomerSupportBot.A2AClient.Sample -- --verify

using System.Diagnostics;
using CustomerSupportBot.A2AClient.Sample.A2A;
using CustomerSupportBot.A2AClient.Sample.Agents;
using CustomerSupportBot.A2AClient.Sample.Observability;
using CustomerSupportBot.A2AClient.Sample.Verification;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using OpenAI;
using Polly;

// ContentRootPath ACIKCA verilir: varsayilan, sureci baslattigin DIZINDIR. Bu ornek
// tipik olarak depo kokunden `dotnet run --project samples/...` ile calistirilir; o zaman
// varsayilan kok dizin olur ve buradaki appsettings.json HIC OKUNMAZDI — yapilandirma
// sessizce yok sayilir, "anahtari yazdim ama gormuyor" seklinde ortaya cikardi.
//
// Ortam varsayilani da Development'a cekilir: appsettings.Development.json (gitignore'lu,
// anahtar orada durur) yalnizca o ortamda yuklenir. Varsayilan Production birakilsaydi
// gelistiricinin yazdigi anahtar dosyasi okunmazdi.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    EnvironmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? Environments.Development
});
builder.Configuration.AddUserSecrets<Program>(optional: true);

var verifyOnly = args.Contains("--verify");
var positional = args.FirstOrDefault(a => !a.StartsWith("--"));
var baseUrl = (positional ?? builder.Configuration["A2A:BaseUrl"] ?? "http://localhost:5021").TrimEnd('/');
builder.Configuration["A2A:BaseUrl"] = baseUrl;

builder.Services.AddSampleTelemetry(builder.Configuration);
builder.Services.AddSingleton<A2ATokenProvider>();
builder.Services.AddSingleton<RemoteAgentCatalog>();
builder.Services.AddTransient<PartnerTokenHandler>();
builder.Services.AddTransient<SubjectTokenHandler>();

// Kimlik uclari — token'in kendisini alan istemci, token EKLEMEZ (dongu olurdu).
builder.Services.AddHttpClient("auth", c => c.BaseAddress = new Uri(baseUrl));

// A2A cagrilari icin iki ayri boru hatti: urun ajani PARTNER token'i, siparis/sikayet
// ajanlari OZNE token'i ister. Tek bir istemci ikisine birden hizmet edemez.
foreach (var (name, withHandler) in new (string, bool)[] { ("a2a-partner", true), ("a2a-subject", false) })
{
    var b = builder.Services.AddHttpClient(name, c =>
    {
        c.BaseAddress = new Uri(baseUrl);
        // A2A cagrisi uzak tarafta bir LLM calistirir; varsayilan 100sn kisa kalabilir.
        c.Timeout = TimeSpan.FromSeconds(120);
    });

    if (withHandler) b.AddHttpMessageHandler<PartnerTokenHandler>();
    else b.AddHttpMessageHandler<SubjectTokenHandler>();

    b.AddStandardResilienceHandler(o =>
    {
        o.Retry.MaxRetryAttempts = 2;
        o.Retry.BackoffType = DelayBackoffType.Exponential;
        o.Retry.UseJitter = true;
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(60);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(120);
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(120);
    });
}

using var host = builder.Build();

// StartAsync ZORUNLU: AddOpenTelemetry (OpenTelemetry.Extensions.Hosting) TracerProvider'ı bir
// IHostedService üzerinden kurar; host hiç başlatılmazsa o servis hiç çalışmaz ve
// Telemetry:Enabled=true olsa bile ActivitySource.StartActivity dinleyicisiz kalıp sessizce
// no-op döner — "açık" görünen bir ayar hiçbir şey yapmaz. Ölçüldü: yalnızca bu satır eklenince
// konsol ihracatçısı span basmaya başladı. "using var host" bloğu çıkışta StopAsync'i de
// (varsayılan kapanma süresiyle) otomatik çağırır, ihracat akışı yarıda kesilmez.
await host.StartAsync();

var config = host.Services.GetRequiredService<IConfiguration>();
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

Console.WriteLine();
Console.WriteLine("  CustomerSupportBot — A2A partner istemcisi");
Console.WriteLine($"  Sunucu : {baseUrl}");
Console.WriteLine($"  Izleme : {(config.GetSection("Telemetry").GetValue("Enabled", false) ? "acik (Telemetry:Enabled)" : "kapali — acmak icin Telemetry:Enabled=true")}");

if (verifyOnly)
{
    Console.WriteLine("  Mod    : dogrulama (--verify)");
    return await EndToEndCheck.RunAsync(host.Services, baseUrl, cts.Token);
}

// ─── Etkilesimli ajan modu ─────────────────────────────────────────────
var apiKey = config["AI:OpenAI:ApiKey"];
if (string.IsNullOrWhiteSpace(apiKey))
    apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.WriteLine();
    Console.WriteLine("  HATA: LLM anahtari yok. Etkilesimli mod YEREL bir ajan calistirir ve bunun icin");
    Console.WriteLine("        bir model anahtari gerekir. Sunlardan biriyle verin:");
    Console.WriteLine("          export OPENAI_API_KEY=sk-...");
    Console.WriteLine("          dotnet user-secrets set \"AI:OpenAI:ApiKey\" \"sk-...\"");
    Console.WriteLine();
    Console.WriteLine("        Anahtar olmadan zinciri dogrulamak icin:  dotnet run -- --verify");
    return 2;
}

var model = config["AI:OpenAI:Model"] ?? "gpt-4o-mini";
var catalog = host.Services.GetRequiredService<RemoteAgentCatalog>();

Console.WriteLine($"  Model  : {model}");
Console.WriteLine("  Uzak ajanlar kesfediliyor...");

AIAgent agent;
try
{
    var remotes = new List<(AIAgent Agent, A2A.AgentCard Card)>();
    foreach (var spec in new[] { RemoteAgentCatalog.Product, RemoteAgentCatalog.Order, RemoteAgentCatalog.Complaint })
        remotes.Add(await catalog.ResolveAsync(spec, cts.Token));

    var chat = new OpenAIClient(apiKey).GetChatClient(model).AsIChatClient();
    agent = SupportAgentFactory.Create(chat, remotes);
}
catch (Exception ex)
{
    Console.WriteLine($"\n  HATA: uzak ajanlar hazirlanamadi — {ex.Message}");
    Console.WriteLine("  Uygulama calisiyor mu?  A2A:Enabled=true mi?");
    return 1;
}

// Konusma surekliligi: ayni oturum boyunca ayni AgentSession kullanilir, boylece
// "peki onun fiyati neydi" gibi bir devam sorusu baglami koruyabilir.
var session = await agent.CreateSessionAsync(cts.Token);

Console.WriteLine();
Console.WriteLine($"  Hazir. Aktif musteri: {host.Services.GetRequiredService<A2ATokenProvider>().CustomerId}");
Console.WriteLine("  Ornek: 'Cay fiyati nedir?'  ·  'Son siparisim ne durumda?'  ·  'Sikayetlerim neler?'");
Console.WriteLine("  Cikis: 'q' veya bos satir.");
Console.WriteLine();

while (!cts.IsCancellationRequested)
{
    Console.Write("> ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input) || input.Trim().Equals("q", StringComparison.OrdinalIgnoreCase))
        break;

    // Her tur kendi correlation.id'sini alır: bir soruna bakarken "hangi çağrı hangi soruya
    // aitti" sorusunu, konsol çıktısını sunucu loglarıyla/izleriyle eşleştirerek cevaplayabilmek
    // içindir. Activity, HTTP istemci enstrümantasyonu açıkken (Telemetry:Enabled=true) giden
    // A2A çağrılarına W3C traceparent olarak da yansır — sunucu tarafında AYNI iz altında görünür.
    var correlationId = Guid.NewGuid().ToString("N")[..12];
    using var turnActivity = SampleTelemetry.Source.StartActivity("chat-turn");
    turnActivity?.SetTag("correlation.id", correlationId);
    turnActivity?.SetTag("customer.id", host.Services.GetRequiredService<A2ATokenProvider>().CustomerId);

    try
    {
        var reply = await agent.RunAsync(input.Trim(), session, cancellationToken: cts.Token);
        var text = reply.Text?.Trim();
        Console.WriteLine(string.IsNullOrWhiteSpace(text) ? "(bos yanit)" : text);
        Console.WriteLine($"  [correlation.id={correlationId}]");
    }
    catch (OperationCanceledException) { break; }
    catch (Exception ex)
    {
        turnActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        Console.WriteLine($"HATA: {ex.Message}");
        Console.WriteLine($"  [correlation.id={correlationId}]");
    }
    Console.WriteLine();
}

Console.WriteLine("Gorusuruz.");
return 0;

// Host.CreateApplicationBuilder + user-secrets icin gerekli.
public partial class Program;

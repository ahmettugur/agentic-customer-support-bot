using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using CustomerSupportBot.Web;
using CustomerSupportBot.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

if (builder.HostEnvironment.IsDevelopment())
    builder.Logging.SetMinimumLevel(LogLevel.Debug);

TaskScheduler.UnobservedTaskException += (_, args) =>
{
    Console.Error.WriteLine($"[UnobservedTaskException] {args.Exception}");
    args.SetObserved();
};

// ── Auth ────────────────────────────────────────────────────────────────────
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthTokenStore>();
builder.Services.AddScoped<AppAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(
    sp => sp.GetRequiredService<AppAuthStateProvider>());

// ── HTTP ─────────────────────────────────────────────────────────────────────
// AuthorizedHttpClientHandler, DelegatingHandler olarak zincire ekleniyor.
// AuthService kendi içinde ham HttpClient kullandığı için ayrı kayıt gerekiyor.
builder.Services.AddScoped<AuthorizedHttpClientHandler>();

// API adresi wwwroot/appsettings{.Environment}.json'dan (Api:BaseUrl) okunur — eskiden koda
// gömülüydü ve her ortam için yeniden derleme gerektiriyordu. Boş bırakılırsa uygulamanın
// kendi origin'i kullanılır (API ile aynı host'tan, ters vekil arkasında sunulduğunda).
// NOT: wwwroot altındaki yapılandırma tarayıcıya açıktır; buraya gizli değer konmaz.
var apiBaseUrl = builder.Configuration["Api:BaseUrl"];
var apiBase = new Uri(string.IsNullOrWhiteSpace(apiBaseUrl)
    ? builder.HostEnvironment.BaseAddress
    : apiBaseUrl);

builder.Services.AddScoped(sp =>
{
    var handler = sp.GetRequiredService<AuthorizedHttpClientHandler>();
    handler.InnerHandler = new HttpClientHandler();
    return new HttpClient(handler)
    {
        BaseAddress = apiBase
    };
});

// AuthService ham HttpClient'e ihtiyaç duyar (refresh döngüsünü önlemek için)
builder.Services.AddScoped(sp => new AuthService(
    new HttpClient { BaseAddress = apiBase },
    sp.GetRequiredService<AuthTokenStore>()
));

// ── UI Servisleri ────────────────────────────────────────────────────────────
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<ThemeService>();

// ── API Servisleri ────────────────────────────────────────────────────────────
builder.Services.AddScoped<AdminApiService>();
builder.Services.AddScoped<AnalyticsApiService>();
builder.Services.AddScoped<ChatApiService>();
builder.Services.AddScoped<TracesApiService>();
builder.Services.AddScoped<SlaApiService>();
builder.Services.AddScoped<KnowledgeApiService>();

await builder.Build().RunAsync();
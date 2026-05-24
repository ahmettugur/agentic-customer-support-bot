# Program.cs & Bootstrap

**Dosyalar:**
- `Program.cs`
- `App.razor`
- `_Imports.razor`
- `wwwroot/index.html`

Blazor WebAssembly başlangıç noktası — DI, routing, auth state setup.

---

## Program.cs

```csharp
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Configuration: API base URL (appsettings.json'dan)
var apiBase = builder.Configuration["Api:BaseUrl"] ?? "https://localhost:7095";

// Auth state altyapısı
builder.Services.AddSingleton<AuthTokenStore>();
builder.Services.AddScoped<AppAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<AppAuthStateProvider>());
builder.Services.AddAuthorizationCore();

// Auth service — DelegatingHandler kullanmaz (refresh loop önleme)
builder.Services.AddHttpClient<AuthService>(client =>
{
    client.BaseAddress = new Uri(apiBase);
});

// AuthorizedHttpClientHandler — diğer tüm HttpClient'lar için
builder.Services.AddTransient<AuthorizedHttpClientHandler>();

// API service'leri
builder.Services.AddHttpClient<AdminApiService>(c => c.BaseAddress = new Uri(apiBase))
    .AddHttpMessageHandler<AuthorizedHttpClientHandler>();
builder.Services.AddHttpClient<ChatApiService>(c => c.BaseAddress = new Uri(apiBase))
    .AddHttpMessageHandler<AuthorizedHttpClientHandler>();
builder.Services.AddHttpClient<TracesApiService>(...).AddHttpMessageHandler<AuthorizedHttpClientHandler>();
builder.Services.AddHttpClient<SlaApiService>(...).AddHttpMessageHandler<AuthorizedHttpClientHandler>();
builder.Services.AddHttpClient<AnalyticsApiService>(...).AddHttpMessageHandler<AuthorizedHttpClientHandler>();
builder.Services.AddHttpClient<WorkflowApiService>(...).AddHttpMessageHandler<AuthorizedHttpClientHandler>();

// Hata observability
TaskScheduler.UnobservedTaskException += (_, e) =>
{
    Console.Error.WriteLine($"[UnobservedTaskException] {e.Exception}");
    e.SetObserved();
};

await builder.Build().RunAsync();
```

---

## DI yaşam döngüsü

| Service | Lifetime | Sebep |
|---|---|---|
| `AuthTokenStore` | **Singleton** | localStorage tek doğruluk kaynağı |
| `AppAuthStateProvider` | Scoped | Component tree boyunca state |
| `AuthService` | Scoped (via AddHttpClient) | Standalone HttpClient — DelegatingHandler yok |
| `AuthorizedHttpClientHandler` | Transient | Her HttpClient çağrısında yeni instance |
| Diğer API service'ler | Scoped (via AddHttpClient) | İçinde state yok, HttpClient inject |

> ⚠️ **Blazor WASM "Scoped"** = uygulama yaşam döngüsü kadar (tek user, tek tab). Server-side'daki request scope'tan farklı.

### Neden AuthService DelegatingHandler kullanmıyor?

```csharp
builder.Services.AddHttpClient<AuthService>(client => { client.BaseAddress = new Uri(apiBase); });
// AddHttpMessageHandler<AuthorizedHttpClientHandler>() YOK!
```

`AuthService` refresh token endpoint'ini çağırır. Eğer `AuthorizedHttpClientHandler` ile sarılı olsaydı:

```
HttpClient.Send → AuthorizedHandler → Bearer token ekle (expired token!)
   → 401 unauthorized
   → AuthService.TryRefreshAsync() çağrılır
   → HttpClient.Send → AuthorizedHandler → Bearer token (yine expired!)
   → 401 → refresh → 401 → refresh → SONSUZ DÖNGÜ
```

Bu yüzden `AuthService` **raw HttpClient** kullanır — token interception yok.

---

## App.razor

Root component — routing + auth state cascade.

```razor
<CascadingAuthenticationState>
    <Router AppAssembly="@typeof(App).Assembly">
        <Found Context="routeData">
            <AuthorizeRouteView RouteData="@routeData" DefaultLayout="@typeof(EmptyLayout)">
                <NotAuthorized>
                    <RedirectToLogin />
                </NotAuthorized>
                <Authorizing>
                    <p>Yetkilendiriliyor…</p>
                </Authorizing>
            </AuthorizeRouteView>
            <FocusOnNavigate RouteData="@routeData" Selector="h1" />
        </Found>
        <NotFound>
            <LayoutView Layout="@typeof(EmptyLayout)">
                <NotFound />
            </LayoutView>
        </NotFound>
    </Router>
</CascadingAuthenticationState>
```

### Önemli parçalar

| Element | Görev |
|---|---|
| `CascadingAuthenticationState` | `AuthenticationState`'i tüm component tree'ye yayar |
| `AuthorizeRouteView` | `[Authorize]` route'ları kontrol eder |
| `NotAuthorized` | Yetki yoksa `RedirectToLogin` |
| `Authorizing` | Token check ederken kısa loading state |
| `FocusOnNavigate` | Erişilebilirlik — sayfa değişince `h1`'e focus |

### ErrorBoundary

```razor
<ErrorBoundary>
    <ChildContent>
        @* asıl içerik *@
    </ChildContent>
    <ErrorContent>
        <div>Bir hata oluştu. <button onclick="...">Tekrar dene</button></div>
    </ErrorContent>
</ErrorBoundary>
```

Component throw ederse kullanıcıya hata sayfası gösterilir — uygulama crash etmez.

---

## _Imports.razor

Tüm Razor dosyalarına otomatik dahil edilen `using`'ler:

```razor
@using System.Net.Http
@using System.Net.Http.Json
@using System.Text.Json
@using Microsoft.AspNetCore.Components.Authorization
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.AspNetCore.Components.WebAssembly.Http
@using Microsoft.JSInterop

@using CustomerSupportBot.Web
@using CustomerSupportBot.Web.Layout
@using CustomerSupportBot.Web.Components
@using CustomerSupportBot.Web.Services
@using CustomerSupportBot.Web.Models
```

Her Razor dosyasında bu using'leri tekrar yazmaya gerek yok.

---

## wwwroot/index.html

Blazor WASM bootstrap.

```html
<!DOCTYPE html>
<html lang="tr">
<head>
    <meta charset="utf-8" />
    <title>Müşteri Destek Botu</title>
    <base href="/" />

    <!-- Google Fonts: Inter -->
    <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap" />

    <!-- Global stiller -->
    <link rel="stylesheet" href="css/app.css" />
    <link rel="stylesheet" href="CustomerSupportBot.Web.styles.css" />
</head>
<body>
    <div id="app">
        <!-- Loading spinner (Blazor yüklenene kadar) -->
        <svg class="spinner">...</svg>
    </div>

    <!-- Blazor framework -->
    <script src="_framework/blazor.webassembly.js"></script>

    <!-- Dynamic script loader (chat ve admin sayfaları için) -->
    <script>
        window.loadScript = function(src, id) {
            return new Promise((resolve, reject) => {
                if (id && document.getElementById(id)) return resolve();
                const s = document.createElement('script');
                s.src = src;
                if (id) s.id = id;
                s.onload = resolve;
                s.onerror = reject;
                document.head.appendChild(s);
            });
        };
    </script>
</body>
</html>
```

### `window.loadScript` neden?

JS dosyaları **lazy load** — sadece ihtiyaç olunca yüklenir:

```csharp
// Chat.razor.cs
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
    {
        await JS.InvokeVoidAsync("loadScript", "/js/chat-bridge.js", "chat-bridge");
        await JS.InvokeVoidAsync("loadScript", "/js/realtime-client.js", "realtime-client");
    }
}
```

Bu sayede `/admin` sayfasını ziyaret eden user gereksiz `realtime-client.js` (PCM audio code) yüklemez. Initial bundle küçük kalır.

### `<base href="/" />`

Blazor routing'in çalışması için zorunlu. Tüm relative URL'ler bu base'e göre çözülür.

---

## Configuration

`wwwroot/appsettings.json`:

```json
{
  "Api": {
    "BaseUrl": "https://localhost:7095"
  }
}
```

Production'da `appsettings.Production.json`:

```json
{
  "Api": {
    "BaseUrl": "https://api.musteridestek.com"
  }
}
```

`Program.cs`:

```csharp
var apiBase = builder.Configuration["Api:BaseUrl"] ?? "https://localhost:7095";
```

Blazor WASM standalone deploy edilirse appsettings static dosya olarak fetch edilir (build sırasında env'e göre yer değiştirir).

---

## Bağlantılar

- [Auth.md](Auth.md) — auth service detayları
- [Services.md](Services.md) — API service'ler
- [JsInterop.md](JsInterop.md) — lazy load edilen JS dosyaları

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
builder.Services.AddScoped<AuthorizedHttpClientHandler>();

builder.Services.AddScoped(sp =>
{
    var handler = sp.GetRequiredService<AuthorizedHttpClientHandler>();
    handler.InnerHandler = new HttpClientHandler();
    return new HttpClient(handler)
    {
        BaseAddress = new Uri("https://localhost:7095")
    };
});

// AuthService ham HttpClient'e ihtiyaç duyar (refresh döngüsünü önlemek için)
builder.Services.AddScoped(sp => new AuthService(
    new HttpClient { BaseAddress = new Uri("https://localhost:7095") },
    sp.GetRequiredService<AuthTokenStore>()
));

// ── API Servisleri ────────────────────────────────────────────────────────────
builder.Services.AddScoped<AdminApiService>();
builder.Services.AddScoped<AnalyticsApiService>();
builder.Services.AddScoped<ChatApiService>();
builder.Services.AddScoped<TracesApiService>();
builder.Services.AddScoped<SlaApiService>();
builder.Services.AddScoped<WorkflowApiService>();

await builder.Build().RunAsync();
```

---

## DI yaşam döngüsü

| Service | Lifetime | Sebep |
|---|---|---|
| `AuthTokenStore` | Scoped | localStorage — tek kullanıcı, tek tab |
| `AppAuthStateProvider` | Scoped | Component tree boyunca state |
| `AuthService` | Scoped (ham HttpClient) | DelegatingHandler yok — refresh döngüsü önlenir |
| `AuthorizedHttpClientHandler` | Scoped | Diğer HttpClient'larla paylaşılır |
| `HttpClient` (authorized) | Scoped | AuthorizedHttpClientHandler ile sarılı |
| Diğer API service'ler | Scoped | İçinde state yok, authorize HttpClient inject |

> **Blazor WASM "Scoped"** = uygulama yaşam döngüsü kadar (tek user, tek tab). Server-side Scoped'tan farklı.

### Neden AuthService DelegatingHandler kullanmıyor?

```csharp
// AuthService için: new HttpClient { BaseAddress = ... }  ← ham, handler YOK
// Diğerleri için: handler.InnerHandler = new HttpClientHandler() ← handler VAR
```

Eğer `AuthorizedHttpClientHandler` ile sarılı olsaydı:

```
401 → TryRefreshAsync → POST /auth/refresh (handler'lı) → token expired → 401 → SONSUZ DÖNGÜ
```

Bu yüzden `AuthService` **raw HttpClient** kullanır — token interception yok.

### API BaseAddress

`Program.cs` hardcode `https://localhost:7095`. Production için:

```csharp
// Seçenek: builder.Configuration["Api:BaseUrl"] ?? "https://localhost:7095"
```

`wwwroot/appsettings.json`:

```json
{ "Api": { "BaseUrl": "https://localhost:7095" } }
```

Production'da `appsettings.Production.json` ile override.

---

## Kayıtlı API Service'leri

| Service | Sorumluluk |
|---|---|
| `AdminApiService` | Admin panel HITL + escalation + agent operasyonları |
| `AnalyticsApiService` | Dashboard + session analytics |
| `ChatApiService` | Chat rating + session metadata |
| `TracesApiService` | Trace dashboard + replay |
| `SlaApiService` | SLA status + events |
| `WorkflowApiService` | Workflow CRUD + test |

Tümü aynı authorize `HttpClient`'ı DI'dan alır — `AuthorizedHttpClientHandler` Bearer token ekler.

---

## Hata observability

```csharp
TaskScheduler.UnobservedTaskException += (_, args) =>
{
    Console.Error.WriteLine($"[UnobservedTaskException] {args.Exception}");
    args.SetObserved();
};
```

Fire-and-forget task exception'larını browser console'a yazar, uygulamanın crash etmesini önler.

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
        </Found>
        <NotFound>
            <LayoutView Layout="@typeof(EmptyLayout)">
                <NotFound />
            </LayoutView>
        </NotFound>
    </Router>
</CascadingAuthenticationState>
```

| Element | Görev |
|---|---|
| `CascadingAuthenticationState` | `AuthenticationState`'i tüm component tree'ye yayar |
| `AuthorizeRouteView` | `[Authorize]` route'ları kontrol eder |
| `NotAuthorized` | Yetki yoksa `RedirectToLogin` |
| `Authorizing` | Token check ederken kısa loading state |

---

## Bağlantılar

- [Auth.md](Auth.md) — auth service detayları
- [Services.md](Services.md) — API service'ler
- [JsInterop.md](JsInterop.md) — lazy load edilen JS dosyaları

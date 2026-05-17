# Frontend — Blazor WebAssembly Admin & Chat UI

Bu doküman `CustomerSupportBot.Web` projesini anlatır. Proje, Blazor WebAssembly (.NET 10) tabanlı bir SPA olup API sunucusuna (`CustomerSupportBot.Api`) HTTP ve SSE üzerinden bağlanır.

---

## 1. Genel Bakış

```
CustomerSupportBot.Web/
├── Pages/          → Razor sayfaları (Chat, Admin, Traces, SLA, Workflow, ...)
├── Layout/         → NavBar, MainLayout, AdminLayout, RedirectToLogin
├── Services/       → API istemci servisleri + Auth katmanı
├── Models/         → DTO'lar (istemci tarafı)
├── Components/     → Paylaşılan bileşenler
├── wwwroot/        → Statik dosyalar (index.html, CSS, JS)
└── Program.cs      → WASM host yapılandırması
```

| Özellik | Değer |
|---------|-------|
| Framework | Blazor WebAssembly (.NET 10) |
| Render modu | Client-side (WASM) |
| API iletişimi | `HttpClient` + SSE streaming |
| Auth | JWT Bearer (access + refresh token) |
| Bağımlılıklar | `Markdig` (Markdown render), `Microsoft.AspNetCore.Components.Authorization` |

---

## 2. Sayfalar

| Sayfa | Dosya | Erişim | Açıklama |
|-------|-------|--------|----------|
| **Chat** | `Pages/Chat.razor` | Herkese açık | Canlı müşteri destek sohbeti; SSE streaming yanıt, rating, oturum yönetimi |
| **Admin** | `Pages/Admin.razor` | Admin / Agent | HITL onay yönetimi, eskalasyon, ajan durumu, müşteri profili, self-improvement |
| **Traces** | `Pages/Traces.razor` | Admin | Reasoning trace listesi ve detay görünümü |
| **Replay** | `Pages/Replay.razor` | Admin | Geçmiş oturumları adım adım tekrar oynatma |
| **SLA** | `Pages/Sla.razor` | Admin | SLA Guardian olay akışı ve ihlal listesi |
| **Workflow Designer** | `Pages/WorkflowDesigner.razor` | Admin | Low-code workflow tanımlama arayüzü |
| **Login** | `Pages/Login.razor` | Herkese açık | JWT tabanlı kullanıcı girişi |

---

## 3. Layout Yapısı

```
Layout/
├── MainLayout.razor       → Chat sayfası için ana düzen (NavMenu dahil)
├── AdminLayout.razor      → Admin sayfaları için düzen
├── AdminNavBar.razor      → Admin üst navigasyon çubuğu
├── NavMenu.razor          → Sol kenar çubuğu (Chat, Traces, Admin linkleri)
├── EmptyLayout.razor      → Login sayfası gibi nav içermeyen sayfalar için
└── RedirectToLogin.razor  → Kimliği doğrulanmamış kullanıcıyı /login'e yönlendirir
```

---

## 4. Servisler

### HTTP İstemcisi

`Program.cs`'de iki ayrı `HttpClient` kaydedilir:

| İstemci | Amaç | Handler |
|---------|------|---------|
| **Yetkili** (`HttpClient`) | API çağrılarının büyük çoğunluğu | `AuthorizedHttpClientHandler` — her istekte `Authorization: Bearer <token>` ekler |
| **Ham** (`new HttpClient`) | `AuthService` — token yenileme döngüsünü önlemek için auth header'ı eklenmez | — |

```csharp
// Program.cs
builder.Services.AddScoped<AuthorizedHttpClientHandler>();
builder.Services.AddScoped(sp => new HttpClient(handler)
{
    BaseAddress = new Uri("https://localhost:7095")
});
```

### API Servisleri

| Servis | Dosya | Kapsar |
|--------|-------|--------|
| `AdminApiService` | `Services/AdminApiService.cs` | Approval, Escalation, Agent, Session, Customer Profile, Personalization, Memory (Lessons), Workflow endpoint'leri; rol bazlı prefix (`/agent` vs `/`) |
| `ChatApiService` | `Services/ChatApiService.cs` | SSE chat streaming, rating CRUD, session/mesaj listesi |
| `TracesApiService` | `Services/TracesApiService.cs` | Reasoning trace listesi ve detay |
| `AnalyticsApiService` | `Services/AnalyticsApiService.cs` | Analitik veriler |
| `SlaApiService` | `Services/SlaApiService.cs` | SLA olay akışı |
| `WorkflowApiService` | `Services/WorkflowApiService.cs` | Workflow tanım CRUD, tetikleme |

### Auth Katmanı

| Bileşen | Sorumluluk |
|---------|-----------|
| `AuthService` | `/auth/login`, `/auth/refresh`, `/auth/logout` çağrıları; token saklama |
| `AuthTokenStore` | `localStorage`'da `accessToken` + `refreshToken` yönetimi |
| `AppAuthStateProvider` | `AuthenticationStateProvider` implementasyonu; JWT'den claim parse eder |
| `AuthorizedHttpClientHandler` | `DelegatingHandler`; access token'ı header'a ekler, 401 durumunda refresh token akışını tetikler |

---

## 5. SSE Streaming

Chat sayfası yanıtları Server-Sent Events (SSE) olarak alır. `ChatApiService.StreamChatAsync` Blazor WASM'in `BrowserRequestStreamingEnabled` özelliğini kullanarak chunk'ları gerçek zamanlı işler:

```csharp
req.SetBrowserRequestStreamingEnabled(true);
var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
await using var stream = await resp.Content.ReadAsStreamAsync(ct);
```

SSE olay türleri:

| Olay | Açıklama |
|------|----------|
| `agent` | Hangi ajanın çalıştığı ve durumu (`running` / `done`) |
| `response_start` | Yanıt akışı başlıyor |
| `response_delta` | Metin chunk'ı (gerçek zamanlı yazı efekti) |
| `response_complete` | Yanıt tamamlandı, final metin |
| `error` | Hata mesajı |

---

## 6. Rol Bazlı Erişim

`AppAuthStateProvider` JWT içindeki `role` claim'ini okur:

| Rol | Erişim |
|-----|--------|
| `Admin` | Tüm sayfalar + `/approvals`, `/escalations`, `/agents`, `/traces`, `/improvements`, vb. |
| `Agent` | Admin sayfası (sınırlı) + `/agent/approvals`, `/agent/escalations` prefix'i ile çalışır |
| Anonim | Yalnızca Chat ve Login |

`AdminApiService.PrefixAsync()` methodu, aktif rolü okuyarak endpoint prefix'ini otomatik seçer.

---

## 7. Bağımlılık Yapılandırması

### API Taban Adresi

```csharp
// Program.cs
new Uri("https://localhost:7095")  // Development
```

Production'da bu adresin ortam değişkeni veya derleme zamanı konfigürasyonu ile değiştirilmesi gerekir (ör. `appsettings.Production.json` veya CI/CD pipeline değişkeni).

### CORS İlişkisi

`CustomerSupportBot.Api`'deki CORS policy, bu frontend'in origin'ine izin vermelidir. Production yapılandırması:

```json
// CustomerSupportBot.Api/appsettings.json
{
  "Cors": {
    "AllowedOrigins": [
      "https://your-blazor-app.example.com"
    ]
  }
}
```

Detay → [persistence.md#9-cors-yapılandırması](persistence.md#9-cors-yapılandırması).

---

## 8. Proje Referansı

`CustomerSupportBot.Web.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Markdig" Version="0.37.0" />
    <PackageReference Include="Microsoft.AspNetCore.Components.Authorization" Version="10.0.0" />
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly" Version="10.0.0" />
  </ItemGroup>
</Project>
```

`Markdig`, Admin panelinde reasoning trace ve lesson içeriklerini Markdown olarak render etmek için kullanılır.

---

## 9. Geliştirme Kurulumu

```bash
# API sunucusunu başlat (port 7095)
cd CustomerSupportBot.Api
dotnet run

# Blazor WASM'i başlat (ayrı terminal)
cd CustomerSupportBot.Web
dotnet run
# → https://localhost:5173 (veya launchSettings'teki port)
```

WASM uygulaması, statik dosyaları API sunucusunun `wwwroot/` klasöründen sunabilir. `CustomerSupportBot.Api/wwwroot/` dizininde `index.html` ve Blazor bootstrap dosyaları varsa ayrı bir WASM sunucusu gerekmez; API sunucusu her ikisini de host eder.

---

## Çapraz Referanslar

- **API endpoint'leri** → [api.md](api.md)
- **Auth yapısı** → [security.md](security.md)
- **HITL / Onay akışı** → [workflow.md](workflow.md)
- **SLA Guardian** → [runtime.md](runtime.md)
- **Workflow Designer** → [workflow-designer.md](workflow-designer.md)
- **Mimari** → [architecture.md](architecture.md)

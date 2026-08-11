# CustomerSupportBot.Web

Blazor WebAssembly (WASM) tabanlı ön yüz katmanı. `CustomerSupportBot.Api` üzerinden sunulan REST endpoint'lerini tüketerek müşteri destek arayüzünü, admin panelini, trace/replay görüntüleyicisini ve bilgi tabanı yönetimini sağlar.

## Katman Sorumluluğu

| Alan | Açıklama |
|------|----------|
| **Çalışma ortamı** | Tarayıcıda çalışan Blazor WASM uygulaması; sunucu tarafı kodu yoktur. |
| **API iletişimi** | `HttpClient` + `AuthorizedHttpClientHandler` üzerinden JWT tabanlı güvenli istekler. |
| **Kimlik doğrulama** | `localStorage`'da JWT saklayan `AuthTokenStore`, otomatik refresh ve yönlendirme. |
| **Tema** | Dark/Light tema desteği (`ThemeService`). |
| **Bildirim** | Toast mesaj sistemi (`ToastService` + `ToastContainer`). |

## Dosya Yapısı

### Program & Giriş Noktası
- [Program.md](Program.md) — WASM host yapılandırması ve DI kaydı.

### Pages
- [Admin.md](Pages/Admin.md) — Admin & Agent paneli (approvals, escalations, chat sessions, analytics, improvements).
- [Chat.md](Pages/Chat.md) — Müşteri chat sayfası (SSE stream, rating, approval bildirimleri).
- [Login.md](Pages/Login.md) — Admin/Agent giriş sayfası.
- [CustomerLogin.md](Pages/CustomerLogin.md) — Müşteri giriş/kayıt sayfası.
- [Knowledge.md](Pages/Knowledge.md) — Bilgi tabanı (knowledge base) CRUD yönetimi.
- [Replay.md](Pages/Replay.md) — Reasoning trace adım adım replay görüntüleyici.
- [Sla.md](Pages/Sla.md) — SLA dashboard (ihlal/uyarı durumları, olay listesi).
- [Traces.md](Pages/Traces.md) — Trace oturumları listesi ve detay paneli.

### Components
- [ToastContainer.md](Components/ToastContainer.md) — Toast bildirim render bileşeni.
- [TraceDetailPanel.md](Components/TraceDetailPanel.md) — Tek bir trace'in detaylı görüntülendiği yan panel.
- [TraceSessionItem.md](Components/TraceSessionItem.md) — Trace listesi satır bileşeni.

### Layout
- [AdminLayout.md](Layout/AdminLayout.md) — Admin sayfaları için layout ve navigasyon.
- [MainLayout.md](Layout/MainLayout.md) — Genel sayfa layout'u ve navigasyon menüsü.
- [RedirectToLogin.md](Layout/RedirectToLogin.md) — Yetkisiz kullanıcıları login sayfasına yönlendirme.

### Models
- [AdminModels.md](Models/AdminModels.md) — Admin paneli DTO/record tanımları.
- [KnowledgeModels.md](Models/KnowledgeModels.md) — Bilgi tabanı DTO'ları.
- [TraceDetailModels.md](Models/TraceDetailModels.md) — Trace görüntüleme ve replay modelleri.

### Helpers
- [JsonExtensions.md](Helpers/JsonExtensions.md) — `JsonElement` üzerinde güvenli erişim extension metotları.

### Services
- [AdminApiService.md](Services/AdminApiService.md) — Admin/Agent API istemcisi (approvals, escalations, chat sessions, improvements).
- [AnalyticsApiService.md](Services/AnalyticsApiService.md) — Analytics dashboard API istemcisi.
- [AppAuthStateProvider.md](Services/AppAuthStateProvider.md) — Blazor `AuthenticationStateProvider` uygulaması.
- [AuthService.md](Services/AuthService.md) — Login, logout, token refresh işlemleri.
- [AuthTokenStore.md](Services/AuthTokenStore.md) — `localStorage` tabanlı JWT depolama.
- [AuthorizedHttpClientHandler.md](Services/AuthorizedHttpClientHandler.md) — Otomatik Bearer token ekleme ve 401 refresh.
- [ChatApiService.md](Services/ChatApiService.md) — Chat oturumu, rating ve approval API istemcisi.
- [JwtUtils.md](Services/JwtUtils.md) — JWT `exp` claim'ini imza doğrulamadan decode eden yardımcı (`AppAuthStateProvider`'ın süre kontrolü için).
- [KnowledgeApiService.md](Services/KnowledgeApiService.md) — Bilgi tabanı CRUD API istemcisi.
- [SlaApiService.md](Services/SlaApiService.md) — SLA durum ve olay API istemcisi.
- [ThemeService.md](Services/ThemeService.md) — Dark/Light tema yönetimi.
- [ToastService.md](Services/ToastService.md) — Toast bildirim event yayıncısı.
- [TracesApiService.md](Services/TracesApiService.md) — Trace oturumları ve detay API istemcisi.

## Katman İlişkileri

```
CustomerSupportBot.Web (Blazor WASM)
    │
    │  HTTP (REST + SSE)
    ▼
CustomerSupportBot.Api (ASP.NET Core Backend)
```

- **Bağımlı olduğu katman**: Yalnızca `CustomerSupportBot.Api` — tüm iletişim HTTP üzerinden.
- **Doğrudan kod bağımlılığı**: Yok (WASM izolasyonu). Model tanımları kendi içindedir.

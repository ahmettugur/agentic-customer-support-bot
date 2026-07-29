# CustomerSupportBot.Web

**Blazor WebAssembly** istemcisi — iki kullanıcı:

1. **Müşteri** → `/` (chat sayfası, anonim)
2. **Admin / Agent** → `/login` → `/admin` (HITL paneli, JWT auth)

WASM olarak browser'da çalışır; tüm iş `Api` projesindeki HTTP endpoint'lere fetch/SSE/WebSocket ile yapılır.

---

## Tech stack

| Katman | Kullanılan |
|---|---|
| Framework | Blazor WebAssembly (.NET 10) |
| Auth | JWT (localStorage) + refresh token rotation |
| UI | Razor components + CSS (component-scoped + global) |
| Markdown | Markdig (LLM yanıtlarını render etmek için) |
| Voice | Web Audio API + AudioWorklet (PCM16 24kHz) + WebSocket |
| Live updates | EventSource (SSE) — admin paneli + chat events |
| State | Component-scoped (singleton service yok) |

---

## Klasör yapısı

```
CustomerSupportBot.Web/
├── Program.cs                          ← DI setup
├── App.razor                           ← Router + AuthenticationState
├── _Imports.razor                      ← Global usings
├── Layout/
│   ├── MainLayout.razor                ← (legacy)
│   ├── AdminLayout.razor               ← Admin sayfaları
│   ├── AdminNavBar.razor               ← Top nav
│   ├── EmptyLayout.razor               ← Login + Chat (sade)
│   ├── NavMenu.razor                   ← (legacy sidebar)
│   └── RedirectToLogin.razor           ← [Authorize] fail → /login
├── Pages/
│   ├── Login.razor                     ← /login
│   ├── Chat.razor                      ← / (müşteri)
│   ├── Admin.razor                     ← /admin (HITL paneli)
│   ├── Traces.razor                    ← /traces (reasoning audit)
│   ├── Replay.razor                    ← /replay (step-through)
│   ├── Sla.razor                       ← /sla (SLA dashboard)
│   ├── WorkflowDesigner.razor          ← /workflow-designer
│   ├── WorkflowDefaults.cs             ← Default workflow JSON
│   └── NotFound.razor                  ← 404
├── Components/
│   ├── TraceDetailPanel.razor          ← Trace detayı (sağ panel)
│   └── TraceSessionItem.razor          ← Trace listede tek satır
├── Services/
│   ├── AuthService.cs                  ← Login/Logout/Refresh
│   ├── AppAuthStateProvider.cs         ← Blazor auth state
│   ├── AuthTokenStore.cs               ← localStorage R/W
│   ├── AuthorizedHttpClientHandler.cs  ← Bearer token inject
│   ├── AdminApiService.cs              ← /approvals, /escalations, ...
│   ├── ChatApiService.cs               ← /sessions, /rating
│   ├── AnalyticsApiService.cs          ← /analytics
│   ├── TracesApiService.cs             ← /traces
│   ├── SlaApiService.cs                ← /sla
│   └── WorkflowApiService.cs           ← /workflows
├── Models/
│   ├── AdminModels.cs
│   ├── TraceDetailModels.cs
│   └── WorkflowModels.cs
└── wwwroot/
    ├── index.html                      ← Blazor bootstrap
    ├── css/                            ← Component-scoped + global stiller
    └── js/
        ├── chat-bridge.js              ← Blazor↔JS chat bridge
        ├── admin-chat-bridge.js        ← Admin SSE
        ├── realtime-client.js          ← WebSocket audio
        ├── realtime-pcm-worklet.js     ← AudioWorklet (PCM16)
        ├── realtime-ui.js              ← Voice UI controller
        └── sla-bridge.js               ← SLA SSE
```

---

## Dokümantasyon haritası

| Doküman | Kapsam |
|---|---|
| [Program.md](Program.md) | Program.cs, DI sırası, _Imports, App.razor |
| [Auth.md](Auth.md) | AuthService, AppAuthStateProvider, AuthTokenStore, AuthorizedHttpClientHandler, Login |
| [Layouts.md](Layouts.md) | MainLayout, AdminLayout, AdminNavBar, EmptyLayout, NavMenu, RedirectToLogin |
| [Pages-Chat.md](Pages-Chat.md) | Chat.razor — müşteri arayüzü, voice integration |
| [Pages-Admin.md](Pages-Admin.md) | Admin.razor — HITL paneli (tüm tab'lar) |
| [Pages-Observability.md](Pages-Observability.md) | Traces, Replay, Sla + bileşenleri |
| [Pages-Workflow.md](Pages-Workflow.md) | WorkflowDesigner + WorkflowDefaults |
| [Services.md](Services.md) | 6 API service sınıfı |
| [Models.md](Models.md) | DTO'lar |
| [JsInterop.md](JsInterop.md) | chat-bridge, admin-chat-bridge, realtime-* JS dosyaları |

---

## Routing matrisi

| Path | Sayfa | Layout | Auth |
|---|---|---|---|
| `/` | Chat | EmptyLayout | Anonim |
| `/login` | Login | EmptyLayout | Anonim |
| `/admin` | Admin (HITL) | AdminLayout | Admin/Agent |
| `/traces` | Traces | AdminLayout | Admin/Agent |
| `/replay` | Replay | AdminLayout | Admin/Agent |
| `/sla` | Sla | AdminLayout | Admin/Agent |
| `/workflow-designer` | WorkflowDesigner | AdminLayout | Admin/Agent |
| `/not-found` | NotFound | MainLayout | Anonim |

`NotFound.razor` sabit bir `/not-found` route'una sahiptir; wildcard bir `*` route değildir — eşleşmeyen path'ler `App.razor`'daki `<Router NotFoundPage="typeof(Pages.NotFound)">` attribute'u üzerinden buraya yönlendirilir.

`[Authorize]` attribute fail → `RedirectToLogin` çağırılır → `?return=...` query param ile login'e yönlenir.

---

## Composition pattern

```
Razor Page
   ↓ @inject IApiService
ApiService (Services/)
   ↓ HttpClient
AuthorizedHttpClientHandler        ← Bearer token inject
   ↓
HTTP request → Api projesi (sunucu)
```

Tüm sayfalar **API service injection** ile çalışır — direkt `HttpClient` kullanmazlar. Service katmanı API path'leri ve response parsing'i kapsüller.

---

## JS interop pattern

Blazor WASM C# kodundan JavaScript'e çağrı yapabilir; JS de C#'a callback verebilir.

```
Blazor (C#)
   ↓ IJSRuntime.InvokeAsync("__streamChat", dotNetRef, ...)
JS (chat-bridge.js)
   ↓ fetch /chat/stream (SSE)
   ↓ dotNetRef.invokeMethodAsync("OnStreamEvent", type, data)
Blazor (C#) — [JSInvokable] method
   ↓ StateHasChanged()
UI render
```

`DotNetObjectReference<Component>` ile Razor component referansı JS'e geçer; JS callback için kullanır.

---

## Streaming protokolleri

| Protokol | Endpoint | Kullanım |
|---|---|---|
| **SSE (fetch)** | `/chat/stream` | Chat yanıtı + reasoning streaming |
| **SSE (EventSource)** | `/chat-sessions/{sid}/subscribe` | Admin canlı izleme |
| **SSE (EventSource)** | `/chat/events/{sid}` | Müşteri persistent event'ler (handoff) |
| **SSE (EventSource)** | `/sla/events` | SLA event akışı |
| **WebSocket** | `/chat/realtime`, `/chat/realtime-native` | Voice (PCM16 binary + JSON text) |

---

## Auth akışı

```
1. /login → AuthService.LoginAsync(username, password)
   → POST /auth/login
   → response { accessToken, refreshToken, user }
   → AuthTokenStore.WriteAsync(localStorage)
   → AppAuthStateProvider.NotifyStateChanged()
   ↓
2. /admin → @attribute [Authorize]
   → CascadingAuthenticationState aktif
   → ClaimsPrincipal'da Role claim'i kontrol edilir
   ↓
3. AdminApiService.GetPendingApprovalsAsync()
   → HttpClient (AuthorizedHttpClientHandler ile)
   → Authorization: Bearer <token>
   ↓
4. Token expire → 401 → AuthService.TryRefreshAsync()
   → POST /auth/refresh
   → Yeni token'lar yazılır
   ↓
5. Logout → POST /auth/logout + localStorage clear → /login
```

---

## Bağlantılar

- [Api projesi](../api/README.md) — sunucu tarafı endpoint'ler
- [Domain](../domain/README.md) — paylaşılan iş kavramları

# CustomerSupportBot.Web

Bu klasör, Blazor WebAssembly (.NET 10) üzerinde geliştirilmiş; son kullanıcılar için canlı sohbet (SSE/WebRTC sesli chat) ve yöneticiler/temsilciler için yönetim panelini (HITL onayları, trace izleme, replay, bilgi bankası, SLA panosu) sunan modern web istemcisidir.

## Dizin Yapısı

- [Pages/](Pages/Chat.md) — Blazor Sayfaları:
  - [Chat](Pages/Chat.md) — Son kullanıcı sohbet arayüzü; SSE gerçek zamanlı metin akışı, WebRTC/WebSocket sesli görüşme (PCM16 AudioWorklet) ve ReAct akıl yürütme görselleştirmesi.
  - [Admin](Pages/Admin.md) — Yönetim paneli; HITL onay kuyruğu (Pending onaylar, sipariş/şikayet kabul-red), canlı temsilci eskalasyonları ve telemetri maliyet raporları.
  - [Traces](Pages/Traces.md) — Çoklu ajan trace izleme; ajan süreleri, araç çağrıları ve JSON akıl yürütme adımları.
  - [Replay](Pages/Replay.md) — Geçmiş oturumların adım adım yeniden oynatılması.
  - [Knowledge](Pages/Knowledge.md) — RAG bilgi bankası arama ve makale yönetimi.
  - [Sla](Pages/Sla.md) — SLA performans ve yanıt süresi panosu.
  - [Login](Pages/Login.md) & [CustomerLogin](Pages/CustomerLogin.md) — JWT kimlik doğrulama sayfaları.
- [Services/](Services/ChatApiService.md) — İstemci API İletişim Servisleri:
  - [ChatApiService](Services/ChatApiService.md) — `/api/chat` senkron ve SSE akış istemcisi.
  - [AdminApiService](Services/AdminApiService.md) — HITL onay, eskalasyon ve yönetim API istemcisi.
  - [TracesApiService](Services/TracesApiService.md) — Trace ve oturum verisi çekici.
  - [AuthService](Services/AuthService.md) & [AppAuthStateProvider](Services/AppAuthStateProvider.md) — JWT token ve Blazor AuthenticationState yöneticisi.
  - [ThemeService](Services/ThemeService.md) & [ToastService](Services/ToastService.md) — Koyu/açık tema ve bildirim yöneticileri.
- [Components/](Components/TraceDetailPanel.md) — Paylaşılan UI Bileşenleri (`TraceDetailPanel`, `TraceSessionItem`, `ToastContainer`).
- [Layout/](Layout/MainLayout.md) — Şablon düzenleri (`MainLayout`, `AdminLayout`, `NavMenu`).

## Mimari Yetenekleri

- **Blazor WebAssembly SPA:** Tarayıcı tarafında çalışan, hızlı ve reaktif tek sayfa uygulaması.
- **Canlı SSE Tüketimi:** `fetch` ReadableStream veya EventSource kullanarak token bazlı gerçek zamanlı Türkçe yanıt akışı.
- **Web Audio PCM16 Worklet:** Tarayıcı mikrofonundan 24kHz ham ses toplayıp sunucuya WebSocket üzerinden akıtma ve sunucudan dönen PCM sesleri anında oynatma.
- **Ant Design / Modern UI:** Cam efektli paneller (Glassmorphism), koyu tema, interaktif ReAct düşünce panelleri ve X6 akış şemaları.

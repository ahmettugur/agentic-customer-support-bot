# CustomerSupportBot.Api

Bu klasör, hexagonal mimaride **Driving Adapter (Giriş Adaptörü)** ve **Composition Root (Uygulama Montaj Kökü)** rolünü üstlenen; ASP.NET Core Minimal API uç noktalarını (Endpoints), Server-Sent Events (SSE) yayınını, WebSocket ses akışını, JWT kimlik doğrulamasını ve arkaplan işleyicilerini (Workers) barındıran sunucu katmanıdır.

## Dizin Yapısı

- [Program](Program.md) — WebApplication builder yapılandırması, middleware hattı ve uç nokta haritalaması.
- [Endpoints/](../CustomerSupportBot.Adapters.Redis/README.md) — Minimal API uç noktaları:
  - [ChatAndRealtime](Endpoints/ChatAndRealtime.md) — `/api/chat` (Senkron & SSE Streaming), `/api/realtime/ws` (WebSocket PCM16 ses akışı), `/api/sessions` oturum yönetimi.
  - [AdminAndHitl](Endpoints/AdminAndHitl.md) — `/api/admin/approvals` (HITL onay/red kararları), `/api/admin/escalations`, `/api/agent-panel` (Canlı temsilci sohbet köprüsü).
  - [A2A](Endpoints/A2A.md) — `/api/a2a/*` (Agent-to-Agent protokolü uç noktaları ve API anahtarı doğrulama).
  - [ObservabilityAndTelemetry](Endpoints/ObservabilityAndTelemetry.md) — `/api/traces`, `/api/telemetry/cost`, `/api/evaluation/run`, `/api/sla`.
  - [Intelligence](Endpoints/Intelligence.md) — `/api/memory/search`, `/api/personalization/profile`, `/api/improvements/lessons`, `/api/auth/login`.
- [Services/ChatEventOrchestrator](Services/ChatEventOrchestrator.md) — Çok kanallı sohbet koordinatörü; bot akışı ile insan temsilci modları arasındaki SSE olaylarını ve SignalR/WebSocket mesajlaşmasını yöneten orkestratör.
- [Infrastructure/](../CustomerSupportBot.Adapters.Redis/README.md) — Altyapı yardımcıları:
  - [DomainExceptionHandler](Infrastructure/DomainExceptionHandler.md) — `IExceptionHandler` uygulayıcısı; `DomainException` türlerini RFC 7807 ProblemDetails JSON formatına dönüştüren global hata işleyici.
  - [SseAndWebSockets](Infrastructure/SseAndWebSockets.md) — `SseWriter`, `SseForwarder` ve `WebSocketBrowserChannel` gerçek zamanlı iletişim araçları.
- [Workers/](../CustomerSupportBot.Adapters.Redis/README.md) — Arkaplan servisleri:
  - [KnowledgeBaseIngestor](Workers/KnowledgeBaseIngestor.md) — Markdown bilgi bankasını açılışta Qdrant vektör ambarına aktaran `IHostedService`.
  - [SlaGuardianService](Workers/SlaGuardianService.md) — Yanıtsız kalan veya aşırı uzayan sohbetlerde SLA ihlallerini denetleyen `BackgroundService`.

## Mimari Rolü ve Yetenekleri

- **Minimal API Mimarisi:** Controller sınıfları yerine yüksek performanslı, rotalanmış `MapGroup` Minimal API endpoint'leri.
- **Canlı SSE Token Akışı:** `text/event-stream` protokolü ile MAF ResponseAgent token'larını ve adım adım akıl yürütme (`agent_started`, `tool_called`) olaylarını istemciye anında iletme.
- **WebSocket Gerçek Zamanlı Ses:** OpenAI Realtime API ile tarayıcı Web Audio PCM16 worklet'i arasında çift yönlü ses köprüsü.
- **Global Hata Yakalama:** Domain istisnalarını HTTP durum kodlarına (404, 400, 409, 502, 500) eşleyen `DomainExceptionHandler`.

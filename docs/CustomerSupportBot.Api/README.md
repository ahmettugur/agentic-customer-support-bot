# CustomerSupportBot.Api

Bu klasör, hexagonal mimaride **Driving Adapter (Giriş Adaptörü)** ve **Composition Root (Uygulama Montaj Kökü)** rolünü üstlenen; ASP.NET Core Minimal API uç noktalarını (Endpoints), Server-Sent Events (SSE) yayınını, WebSocket ses akışını, JWT kimlik doğrulamasını ve arkaplan işleyicilerini (Workers) barındıran sunucu katmanıdır.

## Dizin Yapısı

- [Program](Program.md) — WebApplication builder yapılandırması, middleware hattı, açılış güvenlik guard'ları ve uç nokta haritalaması.
- **Endpoints/** — Minimal API uç noktaları:
  - [ChatAndRealtime](Endpoints/ChatAndRealtime.md) — `/chat/`, `/chat/stream`, `/chat/events/{id}` (yazılı sohbet + kalıcı SSE), `/chat/realtime[-native]/{id}` (WebSocket ses akışı), `/sessions/*` oturum yönetimi. **Login zorunlu.**
  - [AdminAndHitl](Endpoints/AdminAndHitl.md) — `/approvals/*` (HITL onay/red kararları), `/escalations/*`, `/chat-sessions/*` (Live Takeover), `/agent/*` (Agent-kapsamlı görünüm).
  - [A2A](Endpoints/A2A.md) — `/a2a/*` (Agent-to-Agent protokolü) ve `/auth/a2a/token-exchange`.
  - [ObservabilityAndTelemetry](Endpoints/ObservabilityAndTelemetry.md) — `/agents`, `/analytics/*`, `/eval/*`, `/sla/*`, `/telemetry/*`, `/traces/*`.
  - [Intelligence](Endpoints/Intelligence.md) — `/auth/*` (staff + müşteri login), `/memory/*` (semantik hafıza + bilgi tabanı), `/customers/*` (kişiselleştirme), `/improvements/*` (öz-iyileştirme).
- [Extensions/](Extensions/) — Composition Root kayıt yardımcıları:
  - [AuthServicesExtensions](Extensions/AuthServicesExtensions.md) — JWT authentication + authorization policy'leri.
  - [ApplicationServicesExtensions](Extensions/ApplicationServicesExtensions.md) — driving port'lar, CORS, JSON, rate-limit policy'leri, hosted service'ler.
  - [AiServicesExtensions](Extensions/AiServicesExtensions.md) — `IChatClient` oluşturma + telemetri dekorasyonu.
  - [İnce extension'lar](Extensions/ThinCompositionRootExtensions.md) — Persistence/Redis/Telemetry adapter geçişleri, health check'ler, migration, A2A hosting kaydı.
- [Infrastructure/](Infrastructure/) — Altyapı yardımcıları:
  - [DomainExceptionHandler](Infrastructure/DomainExceptionHandler.md) — `IExceptionHandler` uygulayıcısı; `DomainException` türlerini RFC 7807 ProblemDetails JSON formatına dönüştüren global hata işleyici.
  - [SseAndWebSockets](Infrastructure/SseAndWebSockets.md) — `SseWriter`, `SseForwarder` ve `WebSocketBrowserChannel` gerçek zamanlı iletişim araçları.
  - [ScenarioLoader](Infrastructure/ScenarioLoader.md) — evaluation-scenarios.yaml okuyucu.
- [Models/](Models/) — HTTP DTO'ları:
  - [AuthDtos](Models/AuthDtos.md) — login/refresh/logout/customer-register/customer-login gövdeleri.
  - [AdminAndEndpointModels](Models/AdminAndEndpointModels.md) — admin panel + genel amaçlı uç DTO'ları.
- [Services/ChatEventOrchestrator](Services/ChatEventOrchestrator.md) — `/chat/events/{sessionId}` kalıcı SSE bağlantısını yöneten orkestratör (HITL olayları, köprü mesajları, oturum sahiplik yeniden-doğrulaması).
- [Workers/](Workers/) — Arkaplan servisleri:
  - [KnowledgeBaseStartupService](Workers/KnowledgeBaseIngestor.md) — Markdown bilgi bankasını açılışta Qdrant vektör ambarına aktaran `IHostedService`.
  - [SlaGuardianService](Workers/SlaGuardianService.md) — Yanıtsız kalan veya aşırı uzayan sohbetlerde SLA ihlallerini periyodik denetleyen `BackgroundService` (cross-pod kilit destekli).

## Mimari Rolü ve Yetenekleri

- **Minimal API Mimarisi:** Controller sınıfları yerine yüksek performanslı, rotalanmış `MapGroup` Minimal API endpoint'leri.
- **Login Zorunlu Müşteri Kanalı:** Chat/session/realtime uçları `RequireAuthorization("Customer"/"SessionAccess")` ile korunur; `customerId` hiçbir zaman istek gövdesinden değil, JWT'deki `linked_customer_id` claim'inden okunur.
- **Canlı SSE Token Akışı:** `text/event-stream` protokolü ile MAF ResponseAgent token'larını ve adım adım akıl yürütme (`agent_started`, `tool_called`) olaylarını istemciye anında iletme.
- **Non-Blocking HITL Onayları:** Onay gerektiren tool çağrıları hemen "onaya gönderildi" döner; admin/agent kararını verdiğinde sonuç `unseen`/`history` uçları ve kalıcı SSE üzerinden bildirim olarak akar.
- **WebSocket Gerçek Zamanlı Ses:** OpenAI Realtime API ile tarayıcı Web Audio PCM16 worklet'i arasında çift yönlü ses köprüsü; köprü (agent pipeline yanıt üretir) ve native (model doğrudan konuşur, salt-okunur tool'lar) olmak üzere iki mod.
- **Agent-to-Agent (A2A) Kanalı:** `A2A:Enabled` bayrağına bağlı, partner/özne token ayrımıyla dış sistemlere ürün/sipariş/şikayet ajanlarını yayınlama.
- **Global Hata Yakalama:** Domain istisnalarını HTTP durum kodlarına (404, 403, 409, 503, 400) eşleyen `DomainExceptionHandler`.
- **Rate Limiting:** `auth` (IP, yapılandırılabilir), `chat` (IP, 20/dk), `general` (IP, 60/dk), `a2a` (partner/özne kimliği, yapılandırılabilir) politikaları.

## Bağlantılar

- [../CustomerSupportBot.Application/README.md](../CustomerSupportBot.Application/README.md)
- [../CustomerSupportBot.Adapters.Agents/README.md](../CustomerSupportBot.Adapters.Agents/README.md)

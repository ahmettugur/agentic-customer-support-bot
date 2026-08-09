# CustomerSupportBot.Api

ASP.NET Core 10 Web API — uygulamanın HTTP/WebSocket/SSE giriş noktası ve **Composition Root**.

İki ana sorumluluk:
1. **Composition Root**: Tüm adapter'ları DI'a kaydet, doğru sıra ile compose et
2. **HTTP boundary**: Minimal API endpoint'leri, JWT auth, exception → HTTP mapping

---

## Klasör yapısı

```
CustomerSupportBot.Api/
├── Program.cs                          ← Bootstrap, middleware, DI orchestration
├── AssemblyInfo.cs                     ← [InternalsVisibleTo("CustomerSupportBot.Api.Tests")]
├── PortAliases.cs                      ← Global usings
├── Endpoints/                          ← 16 endpoint dosyası (Minimal API)
│   ├── AuthEndpoints.cs
│   ├── ChatEndpoints.cs                ← /chat, /chat/stream (SSE)
│   ├── RealtimeEndpoints.cs            ← /chat/realtime (WS)
│   ├── SessionEndpoints.cs
│   ├── AdminEndpoints.cs               ← /approvals, /escalations, /chat-sessions
│   ├── AgentPanelEndpoints.cs          ← /agent/* — agent (insan) paneli
│   ├── AgentsEndpoints.cs              ← /agents (registry CRUD)
│   ├── AnalyticsEndpoints.cs           ← /analytics, /sessions/.../rating
│   ├── ImprovementsEndpoints.cs        ← /improvements (Lesson mining)
│   ├── MemoryEndpoints.cs              ← /memory (semantic memory)
│   ├── PersonalizationEndpoints.cs     ← /customers (CustomerProfile)
│   ├── SlaEndpoints.cs
│   ├── TelemetryEndpoints.cs           ← /telemetry/cost
│   ├── TraceEndpoints.cs               ← /traces (reasoning audit)
│   └── EvaluationEndpoints.cs          ← /eval (scenario testing)
├── Extensions/                         ← DI extension method'ları
│   ├── AiServicesExtensions.cs
│   ├── ApplicationServicesExtensions.cs
│   ├── AuthServicesExtensions.cs
│   ├── HealthCheckExtensions.cs
│   ├── PersistenceServicesExtensions.cs
│   ├── RedisServicesExtensions.cs
│   ├── TelemetryExtensions.cs
│   └── WebApplicationExtensions.cs
├── Infrastructure/
│   ├── DomainExceptionHandler.cs       ← Exception → HTTP ProblemDetails
│   ├── SseWriter.cs                    ← Server-Sent Events format
│   ├── SseForwarder.cs                 ← Thread-safe SSE wrapper
│   ├── WebSocketBrowserChannel.cs      ← WS ↔ IBrowserChannel
│   └── ScenarioLoader.cs               ← YAML eval scenarios
├── Services/
│   └── ChatEventOrchestrator.cs        ← Persistent SSE per session
├── Workers/
│   ├── KnowledgeBaseIngestor.cs        ← Startup KB ingest
│   └── SlaGuardianService.cs           ← Periodic SLA scanner
├── Models/                             ← HTTP DTO'lar (anti-corruption)
│   ├── AdminModels.cs
│   ├── EndpointModels.cs
│   └── Auth/AuthDtos.cs
├── Prompts/                            ← LLM prompt'ları (.md dosyaları)
└── KnowledgeBase/                      ← Embed edilecek KB içerik
```

---

## Dokümantasyon haritası

| Doküman | Kapsam |
|---|---|
| [Program.md](Program.md) | Program.cs — DI sıralaması, middleware order, HITL guard |
| [Extensions.md](Extensions.md) | 7 DI extension dosyası |
| [Infrastructure.md](Infrastructure.md) | DomainExceptionHandler, SseWriter, SseForwarder, WebSocketBrowserChannel, ScenarioLoader |
| [Services.md](Services.md) | ChatEventOrchestrator (per-session SSE) |
| [Workers.md](Workers.md) | KnowledgeBaseIngestor, SlaGuardianService |
| [Models.md](Models.md) | HTTP DTO katmanı |
| [Endpoints-Auth.md](Endpoints-Auth.md) | /auth/login, /auth/refresh, /auth/logout |
| [Endpoints-Chat.md](Endpoints-Chat.md) | /chat, /chat/stream, /chat/realtime, /sessions |
| [Endpoints-Admin.md](Endpoints-Admin.md) | /approvals, /escalations, /chat-sessions, /agents, /agent/* |
| [Endpoints-Observability.md](Endpoints-Observability.md) | /traces, /telemetry, /analytics, /sla, /eval |
| [Endpoints-Improvements.md](Endpoints-Improvements.md) | /improvements, /memory, /customers |
| [Prompts.md](Prompts.md) | LLM prompt template dizini |
| [KnowledgeBase.md](KnowledgeBase.md) | Startup KB içerik dosyaları |
| [PortAliases.md](PortAliases.md) | Global using direktifleri |

---

## Composition Root prensibi

Bu proje **hiçbir port implementasyonu içermez** — tüm gerçek iş `Application`, `Adapters.*`, `Domain` katmanlarında yapılır.

`Api` projesinin tek özel rolü:
- Adapter'ları DI'a kaydet
- Doğru sıra ile compose et (örn. TelemetryChatClient → IChatClient decorator)
- HTTP endpoint'leri port servislere bağla
- Exception → HTTP boundary'sini koru

```
HTTP Request
   ↓
Endpoint handler (1-3 satır)
   ↓
IPortService method çağrısı (Application)
   ↓
Port arayüzü → Adapter implementasyonu
   ↓
Domain logic / DB / LLM / Redis / ...
```

Endpoint kodu **anemic** — iş mantığı yok, sadece HTTP request'i port çağrısına çevir.

---

## Auth katmanları

| Scope | Header / param | Kim |
|---|---|---|
| **Anonymous** | — | Public — chat, login, rating |
| **Authenticated** | `Authorization: Bearer ...` | Login yapmış herkes |
| **Admin** | JWT role=Admin | Admin paneli |
| **Agent** | JWT role=Agent + linked_agent_id | Live takeover yapan insan |
| **AdminOrAgent** | JWT role=Admin veya Agent | İki rolün ortak işlemleri |

JWT token query string ile de gönderilebilir (`?access_token=...`) — EventSource ve WebSocket için (header gönderilemediği için).

---

## Streaming protokolleri

| Protokol | Endpoint örnekleri | Format |
|---|---|---|
| **SSE (one-shot)** | `/chat/stream` | `event: TYPE\ndata: JSON\n\n` |
| **SSE (persistent)** | `/chat/events/{sessionId}` | Aynı, oturum boyu açık |
| **SSE (admin)** | `/chat-sessions/{sid}/subscribe` | Aynı, admin view |
| **WebSocket** | `/chat/realtime/{sid?}` | Binary PCM + JSON Text frames |
| **WebSocket** | `/chat/realtime-native/{sid?}` | Aynı, native mode |

SSE tek yönlü (server→client), WebSocket çift yönlü.

---

## Yaşam döngüsü

```
[Startup]
1. ConfigureServices (DI sequence — Program.cs)
   - Telemetry → AI → Redis → Persistence → Application → Auth
2. Hosted services:
   - PersistenceHydrator (Postgres'te eski state expire)
   - SlaGuardianService (her N saniye SLA tarama)
   - KnowledgeBaseIngestor (opsiyonel, startup'ta KB embed)
3. Middleware pipeline (build-up):
   - ExceptionHandler
   - HealthChecks
   - CORS
   - RateLimiter
   - WebSockets (before auth)
   - Auth / Authz

[Request]
4. Endpoint handler → PortService → Domain → Adapter → DB/LLM
5. Response veya stream (SSE/WS)

[Shutdown]
6. Hosted services Stop
7. ConnectionMultiplexer dispose (Redis)
8. DbContext'ler dispose
```

---

## Önemli config bölümleri

| Section | İçerik |
|---|---|
| `AI` | Provider seçimi, OpenAI/Azure config, Realtime |
| `Persistence` | InMemory veya Postgres provider |
| `ConnectionStrings:Postgres` | DB bağlantısı |
| `ConnectionStrings:Redis` veya `Redis:ConnectionString` | Redis |
| `Jwt` | JWT signing key, issuer, audience, lifetimes |
| `Auth:DefaultAdmin` | İlk admin user'ın username/password |
| `Cors:AllowedOrigins` | CORS whitelist |
| `Telemetry` | OTLP endpoint, pricing tablosu |
| `SemanticMemory` | Qdrant + embedding |
| `SelfImprovement` | LessonMiner config |
| `Sla` | Approval/escalation timeout threshold'ları |
| `Approval` | HITL approval gate config |
| `RateLimiter` | "chat" + "general" policy |

---

## Bağlantılar

- [Application](../application/README.md) — port servisleri
- [Adapters.AI](../adapters-ai/README.md)
- [Adapters.Persistence](../adapters-persistence/README.md)
- [Adapters.Redis](../adapters-redis/README.md)
- [Adapters.Telemetry](../adapters-telemetry/README.md)
- [Adapters.Agents](../adapters-agents/README.md) — agent pipeline
- [Domain](../domain/README.md)

# Ports/Inbound — Giriş (Driving) Portları

Bu klasör, dış dünyanın (Api katmanı — HTTP endpoint'leri, WebSocket köprüleri) Application katmanına girmek için kullandığı sözleşmeleri (primary/driving port) içerir. Api katmanı hiçbir zaman somut servis sınıflarına değil, buradaki arayüzlere bağımlıdır — implementasyonlar `Services/` altında bulunur.

## Chat ve Reasoning

- [IChatPort](IChatPort.md) — Ana chat use case'i (streaming + non-streaming).
- [ChatRequest](ChatRequest.md) / [ChatResponse](ChatResponse.md) — `IChatPort`'un giriş/çıkış DTO'ları.
- [StreamEvent](StreamEvent.md) — SSE streaming event zarfı ve tüm event tipleri (`StreamEventTypes`).
- [IReasoningPort](IReasoningPort.md) — Sorgunun ön-analizi (niyet/plan çıkarımı).
- [ISessionPort](ISessionPort.md) — Oturum yaşam döngüsü ve konuşma geçmişi.
- [IInputGuard](IInputGuard.md) — Kullanıcı girdisinin güvenlik denetimi (prompt injection vb.).

## HITL (Human-in-the-Loop) ve Admin Paneli

- [IApprovalPort](IApprovalPort.md) / [ApprovalDecisionInput](ApprovalDecisionInput.md) — Onay akışı.
- [IEscalationPort](IEscalationPort.md) — Eskalasyon (insan temsilciye devir) yönetimi.
- [IHumanAgentPort](IHumanAgentPort.md) — İnsan temsilci CRUD + reroute.
- [IChatSessionPort](IChatSessionPort.md) — Admin/agent'ın canlı oturum devralma/bırakma/replan akışları.
- [IHitlEventPort](IHitlEventPort.md) — Onay/eskalasyon event aboneliği (SSE/WebSocket köprüsü için).
- [ISlaPort](ISlaPort.md) — SLA durum özeti ve periyodik tarama.
- [IImprovementsPort](IImprovementsPort.md) — Self-improving loop ders onay/red akışı.

## Bilgi Tabanı ve Bellek

- [IKnowledgeBasePort](IKnowledgeBasePort.md) — Bilgi bankası makale yönetimi.
- [IMemoryPort](IMemoryPort.md) — Semantic memory dashboard/yönetim.
- [IPersonalizationPort](IPersonalizationPort.md) — Müşteri profili yönetimi.

## Sesli Sohbet (Realtime)

- [IRealtimeBridge](IRealtimeBridge.md) — Köprü modu (STT/TTS + normal pipeline).
- [IRealtimeNativeBridge](IRealtimeNativeBridge.md) — Native mod (model doğrudan konuşur).

## Gözlemlenebilirlik

- [ITelemetryPort](ITelemetryPort.md) — LLM maliyet/kullanım özeti.
- [ITracePort](ITracePort.md) — Reasoning trace okuma ve istatistik.
- [IAnalyticsPort](IAnalyticsPort.md) — Derecelendirme ve dashboard analitiği.

## Değerlendirme (Evaluation)

- [IEvaluationPort](IEvaluationPort.md) — Senaryo koşturma sözleşmesi.
- [EvaluationModels.cs](EvaluationModels.md) — 7 DTO: `ScenarioFile`, `EvaluationScenario`, `ExpectedToolCallSpec`, `CriterionSpec`, `ScenarioResult`, `CriterionResult`, `EvaluationRunResult`.

## Auth/

- [AuthResponse](Auth/AuthResponse.md) — Token yanıtı (staff + müşteri ortak).
- [ITokenService](Auth/ITokenService.md) — JWT access/refresh token üretimi, yenileme, iptal.
- [IUserService](Auth/IUserService.md) — Staff (admin/agent) kimlik doğrulama.
- [ICustomerAuthService](Auth/ICustomerAuthService.md) — Müşteri kayıt + kimlik doğrulama.

## Bağlantılar

- [Ports/Outbound](../Outbound/README.md) — Bu portların karşılığı olan çıkış (driven) portları.
- [../../README.md](../../README.md) — Application katmanı geneli.

> ⚠️ **Not:** Üst dizindeki `docs/CustomerSupportBot.Application/README.md` dosyası bu klasörü `Ports.md` adlı tekil bir dosyaya link veriyor ve `IChatOrchestratorPort`, `IAgentTeamPort`, `IAuthPort`, `IRealtimeSessionPort` gibi kodda karşılığı olmayan port adları içeriyor — bu README, gerçek dosya bazlı yapıyı ve gerçek arayüz adlarını yansıtır. Üst dizin README'sinin güncellenmesi ayrı bir denetim kapsamındadır.

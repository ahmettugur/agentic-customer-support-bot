# Application Ports (Giriş ve Çıkış Sözleşmeleri)

- **Kaynak:** `CustomerSupportBot.Application/Ports/`
- **Namespace:** `CustomerSupportBot.Application.Ports`

Hexagonal Mimaride portlar, uygulamanın dış dünyayla olan sözleşmelerini (Contracts) tanımlar. Her bir portun bağımsız detaylı dokümanı aşağıda listelenmiştir.

---

## Inbound Portlar (Giriş Portları - Driving)

Sürücü adaptörlerin (ASP.NET Core Minimal API, Blazor Web, Arkaplan servisleri) Application katmanındaki iş akışlarını tetiklemek için çağırdığı arayüzlerdir.

| Port Arayüzü | Detaylı Doküman | Sorumluluk |
|---|---|---|
| `IChatPort` | [Ports/Inbound/IChatPort.md](Ports/Inbound/IChatPort.md) | Uçtan uca senkron ve canlı SSE sohbet işleme sözleşmesi. |
| `IChatSessionPort` | [Ports/Inbound/IChatSessionPort.md](Ports/Inbound/IChatSessionPort.md) | Oturum durumu, mod yönetimi ve zorunlu yeniden planlama sözleşmesi. |
| `IReasoningPort` | [Ports/Inbound/IReasoningPort.md](Ports/Inbound/IReasoningPort.md) | 2 aşamalı niyet analizi, varlık doğrulama ve muhakeme. |
| `IApprovalPort` | [Ports/Inbound/IApprovalPort.md](Ports/Inbound/IApprovalPort.md) | HITL onay kuyruğu listeleme ve karar verme. |
| `IEscalationPort` | [Ports/Inbound/IEscalationPort.md](Ports/Inbound/IEscalationPort.md) | İnsan temsilciye eskalasyon yönetimi. |
| `IHumanAgentPort` | [Ports/Inbound/IHumanAgentPort.md](Ports/Inbound/IHumanAgentPort.md) | Temsilci havuzu ve durum yönetimi. |
| `IHitlEventPort` | [Ports/Inbound/IHitlEventPort.md](Ports/Inbound/IHitlEventPort.md) | Canlı temsilci olay akışı. |
| `IEvaluationPort` | [Ports/Inbound/IEvaluationPort.md](Ports/Inbound/IEvaluationPort.md) | Kalite ve regresyon test senaryolarını çalıştırma. |
| `IAnalyticsPort` | [Ports/Inbound/IAnalyticsPort.md](Ports/Inbound/IAnalyticsPort.md) | Puanlama ve analitik veri sözleşmesi. |
| `IImprovementsPort` | [Ports/Inbound/IImprovementsPort.md](Ports/Inbound/IImprovementsPort.md) | Çıkarılan iyileştirme derslerini listeleme. |
| `IInputGuard` | [Ports/Inbound/IInputGuard.md](Ports/Inbound/IInputGuard.md) | Prompt Injection ve uzunluk güvenlik denetimi. |
| `IKnowledgeBasePort` | [Ports/Inbound/IKnowledgeBasePort.md](Ports/Inbound/IKnowledgeBasePort.md) | Bilgi bankası makalelerini yönetme. |
| `IMemoryPort` | [Ports/Inbound/IMemoryPort.md](Ports/Inbound/IMemoryPort.md) | RAG anlamsal arama ve doküman indeksleme. |
| `IPersonalizationPort` | [Ports/Inbound/IPersonalizationPort.md](Ports/Inbound/IPersonalizationPort.md) | Müşteri profili ve ürün önerileri. |
| `IRealtimeBridge` | [Ports/Inbound/IRealtimeBridge.md](Ports/Inbound/IRealtimeBridge.md) | Realtime ses köprüsü sözleşmesi. |
| `IRealtimeNativeBridge` | [Ports/Inbound/IRealtimeNativeBridge.md](Ports/Inbound/IRealtimeNativeBridge.md) | Native WebRTC/WebSocket ses köprüsü. |
| `ISessionPort` | [Ports/Inbound/ISessionPort.md](Ports/Inbound/ISessionPort.md) | Oturum CRUD ve mesaj geçmişi sözleşmesi. |
| `ISlaPort` | [Ports/Inbound/ISlaPort.md](Ports/Inbound/ISlaPort.md) | SLA metrikleri ve ihlal raporlaması. |
| `ITelemetryPort` | [Ports/Inbound/ITelemetryPort.md](Ports/Inbound/ITelemetryPort.md) | Telemetri ve maliyet verisi sözleşmesi. |
| `ITracePort` | [Ports/Inbound/ITracePort.md](Ports/Inbound/ITracePort.md) | Çoklu ajan trace sorgulama sözleşmesi. |
| `StreamEvent` | [Ports/Inbound/StreamEvent.md](Ports/Inbound/StreamEvent.md) | Canlı SSE olay modeli. |
| `Auth Portları` | [Ports/Inbound/Auth/ICustomerAuthService.md](Ports/Inbound/Auth/ICustomerAuthService.md) | Kimlik doğrulama ve kullanıcı yönetimi sözleşmeleri. |

---

## Outbound Portlar (Çıkış Portları - Driven)

Application katmanının veritabanı, LLM sağlayıcısı, Redis veya Telemetri gibi altyapı hizmetlerine erişmek için ihtiyaç duyduğu arayüzlerdir.

| Port Arayüzü | Detaylı Doküman | Sorumluluk |
|---|---|---|
| `IReasoningChatClient` | [Ports/Outbound/AI/IReasoningChatClient.md](Ports/Outbound/AI/IReasoningChatClient.md) | o-serisi muhakeme modeli istemci sözleşmesi. |
| `IGeneralChatClient` | [Ports/Outbound/AI/IGeneralChatClient.md](Ports/Outbound/AI/IGeneralChatClient.md) | Genel amaçlı LLM istemci sözleşmesi. |
| `IEmbeddingPort` | [Ports/Outbound/AI/IEmbeddingPort.md](Ports/Outbound/AI/IEmbeddingPort.md) | Metin vektörleştirme sözleşmesi. |
| `IVectorMemoryPort` | [Ports/Outbound/AI/IVectorMemoryPort.md](Ports/Outbound/AI/IVectorMemoryPort.md) | Vektör arama ve indeksleme sözleşmesi. |
| `IRealtimeVoiceTransport` | [Ports/Outbound/AI/IRealtimeVoiceTransport.md](Ports/Outbound/AI/IRealtimeVoiceTransport.md) | Ses aktarımı sözleşmesi. |
| `IKnowledgeBaseSource` | [Ports/Outbound/AI/IKnowledgeBaseSource.md](Ports/Outbound/AI/IKnowledgeBaseSource.md) | Bilgi bankası kaynak sözleşmesi. |
| `IApprovalQueue` | [Ports/Outbound/Persistence/IApprovalQueue.md](Ports/Outbound/Persistence/IApprovalQueue.md) | HITL onay kuyruğu kalıcılık sözleşmesi. |
| `ICustomerRepository` | [Ports/Outbound/Persistence/ICustomerRepository.md](Ports/Outbound/Persistence/ICustomerRepository.md) | Müşteri veritabanı sözleşmesi. |
| `IOrderRepository` | [Ports/Outbound/Persistence/IOrderRepository.md](Ports/Outbound/Persistence/IOrderRepository.md) | Sipariş veritabanı ve stok sözleşmesi. |
| `IProductCatalogRepository`| [Ports/Outbound/Persistence/IProductCatalogRepository.md](Ports/Outbound/Persistence/IProductCatalogRepository.md)| Ürün kataloğu veritabanı sözleşmesi. |
| `IComplaintRepository` | [Ports/Outbound/Persistence/IComplaintRepository.md](Ports/Outbound/Persistence/IComplaintRepository.md) | Şikayet veritabanı sözleşmesi. |
| `ISessionManager` | [Ports/Outbound/Persistence/ISessionManager.md](Ports/Outbound/Persistence/ISessionManager.md) | Oturum ve mesaj geçmişi kalıcılık sözleşmesi. |
| `IChatBridge` | [Ports/Outbound/Persistence/IChatBridge.md](Ports/Outbound/Persistence/IChatBridge.md) | Canlı temsilci köprü mesajları kalıcılığı. |
| `IChatModeRegistry` | [Ports/Outbound/Persistence/IChatModeRegistry.md](Ports/Outbound/Persistence/IChatModeRegistry.md) | Oturum modu kalıcılık sözleşmesi. |
| `IEscalationSink` | [Ports/Outbound/Persistence/IEscalationSink.md](Ports/Outbound/Persistence/IEscalationSink.md) | Eskalasyon kaydı sözleşmesi. |
| `IHumanAgentRegistry` | [Ports/Outbound/Persistence/IHumanAgentRegistry.md](Ports/Outbound/Persistence/IHumanAgentRegistry.md) | Temsilci havuzu kalıcılık sözleşmesi. |
| `IKnowledgeArticleStore` | [Ports/Outbound/Persistence/IKnowledgeArticleStore.md](Ports/Outbound/Persistence/IKnowledgeArticleStore.md) | Bilgi makalesi kalıcılık sözleşmesi. |
| `ILessonStore` | [Ports/Outbound/Persistence/ILessonStore.md](Ports/Outbound/Persistence/ILessonStore.md) | İyileştirme dersi kalıcılık sözleşmesi. |
| `IRatingStore` | [Ports/Outbound/Persistence/IRatingStore.md](Ports/Outbound/Persistence/IRatingStore.md) | Puanlama kalıcılık sözleşmesi. |
| `ISlaEventSink` | [Ports/Outbound/Persistence/ISlaEventSink.md](Ports/Outbound/Persistence/ISlaEventSink.md) | SLA olay kalıcılık sözleşmesi. |
| `ICustomerProfileStore` | [Ports/Outbound/Persistence/ICustomerProfileStore.md](Ports/Outbound/Persistence/ICustomerProfileStore.md) | Müşteri profili kalıcılık sözleşmesi. |
| `IAppDistributedLock` | [Ports/Outbound/Locking/IAppDistributedLock.md](Ports/Outbound/Locking/IAppDistributedLock.md) | Dağıtık kilit (RedLock) sözleşmesi. |
| `IMessageBusPort` | [Ports/Outbound/Messaging/IMessageBusPort.md](Ports/Outbound/Messaging/IMessageBusPort.md) | Pub/Sub olay yayını sözleşmesi. |
| `IPromptRepository` | [Ports/Outbound/IPromptRepository.md](Ports/Outbound/IPromptRepository.md) | Markdown prompt şablon ambarı sözleşmesi. |
| `ICostCalculatorPort` | [Ports/Outbound/Observability/ICostCalculatorPort.md](Ports/Outbound/Observability/ICostCalculatorPort.md) | Model bazlı token USD maliyet hesaplama. |
| `ICostUsageStorePort` | [Ports/Outbound/Observability/ICostUsageStorePort.md](Ports/Outbound/Observability/ICostUsageStorePort.md) | Anlık kümülatif kullanım ambarı. |
| `ILlmCallPersistencePort` | [Ports/Outbound/Observability/ILlmCallPersistencePort.md](Ports/Outbound/Observability/ILlmCallPersistencePort.md) | LLM çağrı detayları kalıcılığı. |
| `IReasoningTraceStore` | [Ports/Outbound/Observability/IReasoningTraceStore.md](Ports/Outbound/Observability/IReasoningTraceStore.md) | Akıl yürütme trace ambarı kalıcılığı. |

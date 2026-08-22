# CustomerSupportBot.Adapters.Persistence.Postgres

Bu klasör, Application katmanındaki giden (Outbound) kalıcılık portlarının PostgreSQL ve Entity Framework Core 10 ile somutlaştırılmış kalıcı depo implementasyonlarını barındırır.

## Dosyalar ve Gruplar

- [Repositories](Repositories.md) — E-ticaret domain depoları:
  - `CustomerRepository` ➔ [ICustomerRepository](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ICustomerRepository.md)
  - `OrderRepository` ➔ [IOrderRepository](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IOrderRepository.md)
  - `ProductCatalogRepository` ➔ [IProductCatalogRepository](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IProductCatalogRepository.md)
  - `ComplaintRepository` ➔ [IComplaintRepository](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IComplaintRepository.md)
  - `StockDeduction` ➔ Eşzamanlı siparişlerde eksiye düşmeyi önleyen atomik stok düşüm mantığı.
- [HitlAndChat](HitlAndChat.md) — İnsan onay ve canlı sohbet adaptörleri:
  - `PostgresApprovalQueue` ➔ [IApprovalQueue](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IApprovalQueue.md) (Hibrit cache + Redis Pub/Sub + DB)
  - `PostgresEscalationSink` ➔ [IEscalationSink](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IEscalationSink.md)
  - `PostgresHumanAgentRegistry` ➔ [IHumanAgentRegistry](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IHumanAgentRegistry.md)
  - `PostgresChatBridge` ➔ [IChatBridge](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IChatBridge.md)
  - `PostgresChatModeRegistry` ➔ [IChatSessionModeRegistry](../../CustomerSupportBot.Application/Ports/Outbound/Chat/IChatModeRegistry.md)
  - `PostgresSessionManager` ➔ [ISessionManager](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ISessionManager.md)
- [StoresAndSinks](StoresAndSinks.md) — Gözlemlenebilirlik, iyileştirme ve bilgi depoları:
  - `PostgresKnowledgeArticleStore` ➔ [IKnowledgeArticleStore](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IKnowledgeArticleStore.md)
  - `PostgresLessonStore` ➔ [ILessonStore](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ILessonStore.md)
  - `PostgresReasoningTraceStore` ➔ [IReasoningTraceStore](../../CustomerSupportBot.Application/Ports/Outbound/Observability/IReasoningTraceStore.md)
  - `PostgresLlmCallUsageSink` ➔ [ILlmCallPersistencePort](../../CustomerSupportBot.Application/Ports/Outbound/Observability/ILlmCallPersistencePort.md)
  - `PostgresRatingStore` ➔ [IRatingStore](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IRatingStore.md)
  - `PostgresSlaEventSink` ➔ [ISlaEventSink](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ISlaEventSink.md)
  - `PostgresCustomerProfileStore` ➔ [ICustomerProfileStore](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ICustomerProfileStore.md)

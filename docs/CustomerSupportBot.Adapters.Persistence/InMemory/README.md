# CustomerSupportBot.Adapters.Persistence.InMemory

Bu klasör, PostgreSQL veya Redis gibi harici altyapı bağımlılıkları olmadan uygulamanın çalışabilmesini, birim testlerin (Unit Tests) ve entegrasyon testlerinin hızlı koşabilmesini sağlayan bellek içi (InMemory) adaptörleri barındırır. Her sınıf, `../Postgres/` altındaki bir production karşılığıyla **aynı port arayüzünü** uygular; DI kaydına göre biri diğerinin yerini alır (production DI kaydı — `PersistenceAdapterServiceCollectionExtensions` — sadece Postgres implementasyonlarını bağlar; bu sınıflar test projelerinin kendi DI kurulumlarında kullanılır).

## Dosyalar

| Sınıf | Uyguladığı Port | Postgres Karşılığı |
|---|---|---|
| [InMemorySessionManager](InMemorySessionManager.md) | `ISessionManager` | [PostgresSessionManager](../Postgres/PostgresSessionManager.md) |
| [InMemoryApprovalQueue](InMemoryApprovalQueue.md) | `IApprovalQueue` | [PostgresApprovalQueue](../Postgres/PostgresApprovalQueue.md) |
| [InMemoryChatBridge](InMemoryChatBridge.md) | `IChatBridge` | [PostgresChatBridge](../Postgres/PostgresChatBridge.md) |
| [InMemoryChatModeRegistry](InMemoryChatModeRegistry.md) | `IChatModeRegistry` | [PostgresChatModeRegistry](../Postgres/PostgresChatModeRegistry.md) |
| [InMemoryEscalationSink](InMemoryEscalationSink.md) | `IEscalationSink` | [PostgresEscalationSink](../Postgres/PostgresEscalationSink.md) |
| [InMemoryHumanAgentRegistry](InMemoryHumanAgentRegistry.md) | `IHumanAgentRegistry` | [PostgresHumanAgentRegistry](../Postgres/PostgresHumanAgentRegistry.md) |
| [InMemoryCustomerProfileStore](InMemoryCustomerProfileStore.md) | `ICustomerProfileStore` | [PostgresCustomerProfileStore](../Postgres/PostgresCustomerProfileStore.md) |
| [InMemoryLessonStore](InMemoryLessonStore.md) | `ILessonStore` | [PostgresLessonStore](../Postgres/PostgresLessonStore.md) |
| [InMemoryRatingStore](InMemoryRatingStore.md) | `IRatingStore` | [PostgresRatingStore](../Postgres/PostgresRatingStore.md) |
| [InMemoryReasoningTraceStore](InMemoryReasoningTraceStore.md) | `IReasoningTraceStore` | [PostgresReasoningTraceStore](../Postgres/PostgresReasoningTraceStore.md) |
| [InMemorySlaEventSink](InMemorySlaEventSink.md) | `ISlaEventSink` | [PostgresSlaEventSink](../Postgres/PostgresSlaEventSink.md) |
| [InMemoryMessageBusAdapter](InMemoryMessageBusAdapter.md) | `IMessageBusPort` | [RedisMessageBusAdapter](../../CustomerSupportBot.Adapters.Redis/) |

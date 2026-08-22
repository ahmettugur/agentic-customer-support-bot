# CustomerSupportBot.Adapters.Persistence.InMemory

Bu klasör, PostgreSQL veya Redis gibi harici altyapı bağımlılıkları olmadan uygulamanın çalışabilmesini, birim testlerin (Unit Tests) ve entegrasyon testlerinin hızlı koşabilmesini sağlayan bellek içi (InMemory) adaptörleri barındırır. Her sınıf, `../Postgres/` altındaki bir production karşılığıyla **aynı port arayüzünü** uygular; DI kaydına göre biri diğerinin yerini alır (production DI kaydı — `PersistenceAdapterServiceCollectionExtensions` — sadece Postgres implementasyonlarını bağlar; bu sınıflar test projelerinin kendi DI kurulumlarında kullanılır).

## Dosyalar

| Sınıf | Uyguladığı Port | Postgres Karşılığı |
|---|---|---|
| [InMemorySessionManager](InMemorySessionManager.md) | `ISessionManager` | [PostgresSessionManager](../Postgres/HitlAndChat.md) |
| [InMemoryApprovalQueue](InMemoryApprovalQueue.md) | `IApprovalQueue` | [PostgresApprovalQueue](../Postgres/HitlAndChat.md) |
| [InMemoryChatBridge](InMemoryChatBridge.md) | `IChatBridge` | [PostgresChatBridge](../Postgres/HitlAndChat.md) |
| [InMemoryChatModeRegistry](InMemoryChatModeRegistry.md) | `IChatModeRegistry` | [PostgresChatModeRegistry](../Postgres/HitlAndChat.md) |
| [InMemoryEscalationSink](InMemoryEscalationSink.md) | `IEscalationSink` | [PostgresEscalationSink](../Postgres/HitlAndChat.md) |
| [InMemoryHumanAgentRegistry](InMemoryHumanAgentRegistry.md) | `IHumanAgentRegistry` | [PostgresHumanAgentRegistry](../Postgres/HitlAndChat.md) |
| [InMemoryCustomerProfileStore](InMemoryCustomerProfileStore.md) | `ICustomerProfileStore` | [PostgresCustomerProfileStore](../Postgres/StoresAndSinks.md) |
| [InMemoryLessonStore](InMemoryLessonStore.md) | `ILessonStore` | [PostgresLessonStore](../Postgres/StoresAndSinks.md) |
| [InMemoryRatingStore](InMemoryRatingStore.md) | `IRatingStore` | [PostgresRatingStore](../Postgres/StoresAndSinks.md) |
| [InMemoryReasoningTraceStore](InMemoryReasoningTraceStore.md) | `IReasoningTraceStore` | [PostgresReasoningTraceStore](../Postgres/StoresAndSinks.md) |
| [InMemorySlaEventSink](InMemorySlaEventSink.md) | `ISlaEventSink` | [PostgresSlaEventSink](../Postgres/StoresAndSinks.md) |
| [InMemoryMessageBusAdapter](InMemoryMessageBusAdapter.md) | `IMessageBusPort` | [RedisMessageBusAdapter](../../CustomerSupportBot.Adapters.Redis/) |

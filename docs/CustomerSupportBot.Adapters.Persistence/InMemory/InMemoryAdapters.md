# InMemory Kalıcılık Adaptörleri

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/InMemory/*.cs`
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.InMemory`

## Ne işe yararlar?

`InMemory` klasörü altındaki sınıflar, Application katmanının tüm Outbound portlarını `ConcurrentDictionary`, `ConcurrentBag` veya thread-safe listeler kullanarak bellek içinde simüle eden test ve geliştirme adaptörleridir.

---

## Tanımlı InMemory Adaptörleri

| Sınıf Adı | Uyguladığı Port | Bellek Yapısı | Açıklama |
|---|---|---|---|
| `InMemorySessionManager` | [ISessionManager](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ISessionManager.md) | `ConcurrentDictionary<string, AgentSession>` | Oturum ve mesajları bellekte tutar. |
| `InMemoryApprovalQueue` | [IApprovalQueue](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IApprovalQueue.md) | `ConcurrentDictionary<string, QueueEntry>` | Onay kuyruğunu ve olaylarını simüle eder. |
| `InMemoryReasoningTraceStore` | [IReasoningTraceStore](../../CustomerSupportBot.Application/Ports/Outbound/Observability/IReasoningTraceStore.md) | `ConcurrentDictionary<string, ReasoningTrace>` | Trace kayıtlarını hafızada toplar. |
| `InMemoryEscalationSink` | [IEscalationSink](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IEscalationSink.md) | `ConcurrentBag<EscalationRecord>` | Eskalasyonları saklar. |
| `InMemoryHumanAgentRegistry` | [IHumanAgentRegistry](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IHumanAgentRegistry.md) | `List<HumanAgent>` | Temsilci listesini hafızada tutar. |
| `InMemoryChatBridge` | [IChatBridge](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IChatBridge.md) | `ConcurrentQueue<ChatBridgeMessage>` | Köprü mesajlarını saklar. |
| `InMemoryChatModeRegistry` | [IChatSessionModeRegistry](../../CustomerSupportBot.Application/Ports/Outbound/Chat/IChatModeRegistry.md) | `ConcurrentDictionary<string, ChatMode>` | Oturum modunu tutar. |
| `InMemoryLessonStore` | [ILessonStore](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ILessonStore.md) | `List<Lesson>` | İyileştirme derslerini saklar. |
| `InMemoryRatingStore` | [IRatingStore](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IRatingStore.md) | `List<RatingRecord>` | Puanlamaları tutar. |
| `InMemorySlaEventSink` | [ISlaEventSink](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ISlaEventSink.md) | `ConcurrentBag<SlaEvent>` | SLA olaylarını toplar. |
| `InMemoryCustomerProfileStore` | [ICustomerProfileStore](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ICustomerProfileStore.md) | `ConcurrentDictionary<string, CustomerProfile>` | Müşteri profillerini saklar. |
| `InMemoryMessageBusAdapter` | [IMessageBusPort](../../CustomerSupportBot.Application/Ports/Outbound/Messaging/IMessageBusPort.md) | C# `Action<string>` delegeleri | Süreç içi olay yayın/abone adaptörü. |

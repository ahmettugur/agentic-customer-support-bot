# CustomerSupportBot.Adapters.Persistence.Postgres

Bu klasör, Application katmanındaki giden (Outbound) kalıcılık portlarının PostgreSQL ve Entity Framework Core 10 ile somutlaştırılmış implementasyonlarını barındırır. Çoğu sınıf, çok-pod'lu (multi-pod) bir dağıtımda tutarlı çalışabilmek için **hibrit cache + Redis pub/sub + DB'yi tek gerçek kaynak sayan** ortak bir mimari desen kullanır — bkz. [PostgresApprovalQueue](PostgresApprovalQueue.md)'daki ayrıntılı gerekçe blokları, aynı desen diğer dosyalarda tekrar edilmez.

## E-ticaret Domain Depoları

- [CustomerRepository](CustomerRepository.md) ➔ [ICustomerRepository](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ICustomerRepository.md)
- [OrderRepository](OrderRepository.md) ➔ [IOrderRepository](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IOrderRepository.md)
- [ProductCatalogRepository](ProductCatalogRepository.md) ➔ [IProductCatalogRepository](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IProductCatalogRepository.md)
- [ComplaintRepository](ComplaintRepository.md) ➔ [IComplaintRepository](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IComplaintRepository.md)
- [StockDeduction](StockDeduction.md) — Eşzamanlı siparişlerde eksiye düşmeyi önleyen atomik stok düşüm çekirdeği (port yok, internal).

## HITL (İnsan Onayı) ve Canlı Sohbet Adaptörleri

- [PostgresApprovalQueue](PostgresApprovalQueue.md) ➔ [IApprovalQueue](../../CustomerSupportBot.Application/Ports/Outbound/IApprovalQueue.md) — Bloklamayan onay modeli, atomik koşullu sahiplenme.
- [PostgresEscalationSink](PostgresEscalationSink.md) ➔ [IEscalationSink](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IEscalationSink.md)
- [PostgresHumanAgentRegistry](PostgresHumanAgentRegistry.md) ➔ [IHumanAgentRegistry](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IHumanAgentRegistry.md)
- [PostgresChatBridge](PostgresChatBridge.md) ➔ [IChatBridge](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IChatBridge.md) — Canlı devralma mesaj köprüsü.
- [PostgresChatModeRegistry](PostgresChatModeRegistry.md) ➔ [IChatModeRegistry](../../CustomerSupportBot.Application/Ports/Outbound/Chat/IChatModeRegistry.md) — Canlı devralma mod (Bot/Human) yönetimi.
- [PostgresSessionManager](PostgresSessionManager.md) ➔ [ISessionManager](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ISessionManager.md) — Oturum durumu ve konuşma geçmişi.

## Gözlemlenebilirlik, İyileştirme ve Bilgi Depoları

- [PostgresReasoningTraceStore](PostgresReasoningTraceStore.md) ➔ [IReasoningTraceStore](../../CustomerSupportBot.Application/Ports/Outbound/Observability/IReasoningTraceStore.md)
- [PostgresLlmCallUsageSink](PostgresLlmCallUsageSink.md) ➔ [ILlmCallPersistencePort](../../CustomerSupportBot.Application/Ports/Outbound/Observability/ILlmCallPersistencePort.md)
- [PostgresKnowledgeArticleStore](PostgresKnowledgeArticleStore.md) ➔ [IKnowledgeArticleStore](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IKnowledgeArticleStore.md)
- [PostgresLessonStore](PostgresLessonStore.md) ➔ [ILessonStore](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ILessonStore.md)
- [PostgresCustomerProfileStore](PostgresCustomerProfileStore.md) ➔ [ICustomerProfileStore](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ICustomerProfileStore.md)
- [PostgresRatingStore](PostgresRatingStore.md) ➔ [IRatingStore](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IRatingStore.md)
- [PostgresSlaEventSink](PostgresSlaEventSink.md) ➔ [ISlaEventSink](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ISlaEventSink.md)

## Mimari Not: Neden Bu Kadar Çok Sınıfta Aynı "Hibrit" Desen Tekrarlanıyor

Uygulama birden fazla pod (container instance) olarak çalışır. Basit bir `ConcurrentDictionary` cache tek bir pod içinde tutarlıdır ama pod'lar arasında SENKRON DEĞİLDİR — bu yüzden çoğu sınıf üç katmanı birlikte kullanır:

1. **Process-içi cache** (`ConcurrentDictionary`) — hızlı okuma, DB'ye her seferinde gitmeme.
2. **Redis pub/sub** — bir pod'daki değişikliği diğer pod'ların cache'ine anlık yaymak (en-fazla-bir-kez teslimat, mesaj kaybı OLABİLİR).
3. **PostgreSQL** — kayıtların GERÇEK ve kalıcı kaynağı; Redis mesajı kaybolsa bile buradan kurtarma/uzlaştırma yapılabilir.

Bu üçlünün doğru sıralaması ve kritik/kritik-olmayan yazımların ayrımı sınıftan sınıfa farklılaşır — her dosyanın kendi "Tasarım Yaklaşımı" bölümündeki 🐞 notları, o sınıfa özgü hangi hatanın bu deseni gerektirdiğini anlatır.

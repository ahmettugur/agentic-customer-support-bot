# PostgreSQL HITL ve Canlı Sohbet Adaptörleri

- **Kaynaklar:**
  - `CustomerSupportBot.Adapters.Persistence/Postgres/PostgresApprovalQueue.cs`
  - `CustomerSupportBot.Adapters.Persistence/Postgres/PostgresEscalationSink.cs`
  - `CustomerSupportBot.Adapters.Persistence/Postgres/PostgresHumanAgentRegistry.cs`
  - `CustomerSupportBot.Adapters.Persistence/Postgres/PostgresChatBridge.cs`
  - `CustomerSupportBot.Adapters.Persistence/Postgres/PostgresChatModeRegistry.cs`
  - `CustomerSupportBot.Adapters.Persistence/Postgres/PostgresSessionManager.cs`
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.Postgres`

## 1. PostgresApprovalQueue
- **Uyguladığı Port:** [IApprovalQueue](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IApprovalQueue.md)
- **Mimari:** **Hibrit Cache + Redis Pub/Sub + DB Koşullu Sahiplenme**
- **Constructor:**
  ```csharp
  public PostgresApprovalQueue(
      IDbContextFactory<CustomerSupportDbContext> dbFactory,
      IOptions<ApprovalOptions> options,
      IMessageBusPort messageBus,
      IAppDistributedLock distributedLock,
      IApprovalExecutionRouter executionRouter,
      ILogger<PostgresApprovalQueue> logger)
  ```
- **Çalışma Mantığı:**
  - `EnqueueAsync`: Veritabanına `Pending` olarak ekler, yerel `ConcurrentDictionary` ambarına yazar ve `csbot:approval:created` Redis kanalına yayınlar.
  - `DecideAsync`: Çift işlemeyi önlemek için `UPDATE hitl.approvals SET status = @newStatus WHERE id = @id AND status = 'Pending'` koşuluyla atomik sahiplenme (`ClaimDecisionAsync`) yapar. Ardından onaylanmışsa [IApprovalExecutionRouter](../../CustomerSupportBot.Application/Ports/Outbound/IApprovalExecutionRouter.md) üzerinden siparişi/şikayeti işletir ve sonucu `csbot:approval:decided` kanalına basar.

---

## 2. PostgresEscalationSink
- **Uyguladığı Port:** [IEscalationSink](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IEscalationSink.md)
- **Constructor:** `public PostgresEscalationSink(IDbContextFactory<CustomerSupportDbContext> dbFactory)`
- **Çalışma Mantığı:** Canlı temsilciye yönlendirilen talepleri `hitl.escalations` tablosuna kaydeder (`RecordAsync`) ve açık eskalasyonları listeler.

---

## 3. PostgresHumanAgentRegistry
- **Uyguladığı Port:** [IHumanAgentRegistry](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IHumanAgentRegistry.md)
- **Çalışma Mantığı:** Canlı müşteri temsilcilerinin online/offline durumlarını ve aktif sohbet yüklerini (`active_chats_count`) yönetir; en müsait temsilciyi seçer (`GetAvailableAgentAsync`).

---

## 4. PostgresChatBridge & PostgresChatModeRegistry
- **Uyguladığı Portlar:** [IChatBridge](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IChatBridge.md), [IChatSessionModeRegistry](../../CustomerSupportBot.Application/Ports/Outbound/Chat/IChatModeRegistry.md)
- **Çalışma Mantığı:** Oturumun bot veya insan modunda (`Bot`, `Human`, `Hybrid`) olduğunu `chat.chat_session_modes` tablosunda saklar ve temsilci ile müşteri arasındaki köprü mesajlarını `chat.chat_bridge_messages` tablosuna kalıcı olarak yazar.

---

## 5. PostgresSessionManager
- **Uyguladığı Port:** [ISessionManager](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ISessionManager.md)
- **Constructor:** `public PostgresSessionManager(IDbContextFactory<CustomerSupportDbContext> dbFactory)`
- **Çalışma Mantığı:**
  - `GetOrCreateAsync`: Oturumu `chat.sessions` ve mesaj geçmişini `chat.messages` tablosundan yükler.
  - `SaveAsync`: Oturumun `AgentSessionState` verisini JSONB olarak ve yeni gelen mesajları topluca veritabanına kaydeder.

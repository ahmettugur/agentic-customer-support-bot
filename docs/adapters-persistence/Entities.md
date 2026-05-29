# DB Varlık Modelleri (Entities)

Tüm entity sınıfları `EfCore/Entities/` altında şemaya göre gruplandırılmıştır. Her entity bir PostgreSQL tablosuna karşılık gelir.

---

## `chat` şeması

### `SessionEntity` → `chat.sessions`

| Sütun | Tür | Açıklama |
|-------|-----|---------|
| `SessionId` | `varchar` PK | Oturum kimliği |
| `CreatedAt` | `timestamptz` | Oluşturma zamanı |
| `LastActivityAt` | `timestamptz` | Son aktivite |
| `StateJson` | `jsonb` | `SessionState` (intent, sentiment, flags, vb.) |

---

### `MessageEntity` → `chat.messages`

| Sütun | Tür | Açıklama |
|-------|-----|---------|
| `Id` | `bigint` identity PK | Otomatik artan |
| `SessionId` | `varchar` FK→sessions | |
| `Role` | `varchar` | `user` / `assistant` / `system` |
| `Text` | `text` | Mesaj içeriği |
| `CreatedAt` | `timestamptz` | |

---

### `ChatBridgeMessageEntity` → `chat.bridge_messages`

HITL canlı sohbet mesajları. `BotTyping` geçici olduğundan **persist edilmez**.

| Sütun | Tür | Açıklama |
|-------|-----|---------|
| `Id` | `bigint` identity PK | |
| `MessageId` | `varchar(12)` | Domain kimliği (12 karakter rastgele) |
| `SessionId` | `varchar` | |
| `Sender` | `varchar` | `User` / `Bot` / `Admin` / `System` |
| `HumanAgent` | `varchar?` | Admin kimliği (varsa) |
| `Text` | `text` | |
| `CreatedAt` | `timestamptz` | |

---

### `ChatSessionModeEntity` → `chat.session_modes`

| Sütun | Tür | Açıklama |
|-------|-----|---------|
| `SessionId` | `varchar` PK | |
| `Mode` | `varchar` | `Bot` / `Human` |
| `HumanAgent` | `varchar?` | Devralım yapan agent |
| `EnteredAt` | `timestamptz` | Mod değişiklik zamanı |
| `LastActivityAt` | `timestamptz` | |
| `MessageCount` | `int` | Bu mod süresince mesaj sayısı |

---

## `hitl` şeması

### `ApprovalRequestEntity` → `hitl.approval_requests`

| Sütun | Tür | Açıklama |
|-------|-----|---------|
| `Id` | `varchar` PK | |
| `SessionId` | `varchar` | |
| `TraceId` | `varchar?` | |
| `ToolName` | `varchar` | `order_placement_tool` / `complaint_registration_tool` |
| `AgentName` | `varchar?` | |
| `ParametersJson` | `jsonb` | Tool parametreleri |
| `UserQuery` | `text` | Orijinal kullanıcı sorusu |
| `Justification` | `text?` | Bot'un gerekçesi |
| `RequestedAt` | `timestamptz` | |
| `DecidedAt` | `timestamptz?` | |
| `Status` | `varchar` | `Pending` / `Approved` / `Rejected` / `Expired` |
| `DecidedBy` | `varchar?` | |
| `DecisionReason` | `text?` | |
| `TimeoutSeconds` | `int` | SLA timeout süresi |

---

### `EscalationEntity` → `hitl.escalations`

| Sütun | Tür | Açıklama |
|-------|-----|---------|
| `Id` | `varchar` PK | |
| `SessionId` | `varchar` | |
| `TraceId` | `varchar?` | |
| `AgentName` | `varchar?` | Eskalasyonu tetikleyen bot agent |
| `UserQuery` | `text` | |
| `Reason` | `text` | Eskalasyon sebebi |
| `MissingContextJson` | `jsonb` | Eksik bilgiler listesi |
| `ResponseSummary` | `text?` | Bot'un son yanıt özeti |
| `CreatedAt` | `timestamptz` | |
| `AcknowledgedAt` | `timestamptz?` | Human agent devralım zamanı |
| `ResolvedAt` | `timestamptz?` | |
| `Status` | `varchar` | `Open` / `Acknowledged` / `Resolved` / `Dismissed` |
| `AssignedTo` | `varchar?` | Human agent ID |
| `Resolution` | `text?` | Çözüm notu |
| `Priority` | `varchar` | `Low` / `Normal` / `High` / `Critical` |

---

### `HumanAgentEntity` → `hitl.human_agents`

| Sütun | Tür | Açıklama |
|-------|-----|---------|
| `Id` | `varchar` PK | |
| `DisplayName` | `varchar` | |
| `Email` | `varchar?` | |
| `SkillsJson` | `jsonb` | `["complaint", "order", "tr"]` |
| `LanguagesJson` | `jsonb` | `["tr", "en"]` |
| `IsActive` | `bool` | |
| `MaxConcurrentLoad` | `int` | Maksimum eşzamanlı oturum |
| `CurrentLoad` | `int` | Anlık yük |
| `Priority` | `int` | Routing önceliği |
| `CreatedAt` | `timestamptz` | |
| `LastAssignedAt` | `timestamptz?` | |

---

## `observability` şeması

### `ReasoningTraceEntity` → `observability.reasoning_traces`

| Sütun | Tür | Açıklama |
|-------|-----|---------|
| `TraceId` | `varchar` PK | |
| `SessionId` | `varchar` | |
| `UserQuery` | `text` | |
| `StartedAt` | `timestamptz` | |
| `CompletedAt` | `timestamptz?` | |
| `TerminationReason` | `varchar?` | `completed` / `timeout` / `error` / `max_iterations` |
| `FinalResponse` | `text?` | |
| `FirstDraftResponse` | `text?` | Revizyon öncesi ilk taslak yanıt |
| `WasRevised` | `bool` | Yanıt revize edildi mi? |
| `IterationCount` | `int` | |
| `Error` | `text?` | |
| `EstimatedTokens` | `long` | |
| `ReasoningJson` | `jsonb` | `ReasoningResult` tam içeriği |
| `PlanningJson` | `jsonb` | `PlanningResult` |
| `SpecialistReasoningsJson` | `jsonb` | `List<SpecialistReasoning>` |
| `FinalCritiqueJson` | `jsonb` | `ResponseCritique` — yanıt değerlendirmesi |
| `AgentVisitsJson` | `jsonb` | `List<AgentVisit>` |
| `ToolCallsJson` | `jsonb` | Çağrılan tool listesi |

---

### `LlmCallUsageEntity` → `observability.llm_call_usage`

| Sütun | Tür | Açıklama |
|-------|-----|---------|
| `Id` | `bigint` identity PK | |
| `Model` | `varchar` | `gpt-4o`, `o3-mini`, vb. |
| `Provider` | `varchar` | `openai`, `azure` |
| `InputTokens` | `long` | |
| `OutputTokens` | `long` | |
| `CostUsd` | `decimal` | |
| `DurationMs` | `double` | |
| `CalledAt` | `timestamptz` | |

---

## `analytics` şeması

### `RatingEntity` → `analytics.ratings`

| Sütun | Tür | Açıklama |
|-------|-----|---------|
| `SessionId` | `varchar` PK | Bir session için tek rating |
| `Id` | `varchar` | Domain ID |
| `Stars` | `int` | 1–5 |
| `Feedback` | `text?` | Yazılı geri bildirim |
| `RatedAt` | `timestamptz` | |

---

### `SlaEventEntity` → `analytics.sla_events`

| Sütun | Tür | Açıklama |
|-------|-----|---------|
| `Id` | `varchar` PK | |
| `Timestamp` | `timestamptz` | |
| `Kind` | `varchar` | `approval` / `escalation` |
| `Severity` | `varchar` | `warn` / `breach` |
| `TargetId` | `varchar` | İlgili kayıt ID'si |
| `AgeSeconds` | `int` | Olay zamanındaki yaş |
| `Action` | `varchar?` | `AutoReject`, `PriorityBoost`, vb. |
| `Note` | `text?` | Açıklama |

---

## `auth` şeması

### `UserEntity` → `auth.users`

| Sütun | Tür | Açıklama |
|-------|-----|---------|
| `Id` | `varchar` PK | |
| `Username` | `varchar` UNIQUE | |
| `PasswordHash` | `varchar` | BCrypt hash |
| `Role` | `varchar` | `Admin` / `Agent` |
| `LinkedAgentId` | `varchar?` FK→human_agents | Temsilci bağlantısı |
| `IsActive` | `bool` | |
| `CreatedAt` | `timestamptz` | |
| `LastLoginAt` | `timestamptz?` | |

---

### `RefreshTokenEntity` → `auth.refresh_tokens`

| Sütun | Tür | Açıklama |
|-------|-----|---------|
| `Id` | `varchar` PK | |
| `UserId` | `varchar` FK→users | |
| `TokenHash` | `varchar` | SHA-256 hash (plain text hiç saklanmaz) |
| `ExpiresAt` | `timestamptz` | |
| `CreatedAt` | `timestamptz` | |
| `RevokedAt` | `timestamptz?` | Revoke zamanı |
| `ReplacedByTokenHash` | `varchar?` | Rotation zinciri |

---

## `personalization` şeması

### `CustomerProfileEntity` → `personalization.customer_profiles`

| Sütun | Tür | Açıklama |
|-------|-----|---------|
| `CustomerId` | `varchar` PK | |
| `PreferredLanguage` | `varchar?` | `tr` / `en` |
| `PreferredTone` | `varchar?` | `formal` / `casual` / vb. |
| `IntentFrequencyJson` | `jsonb` | `{"order_inquiry": 5, ...}` |
| `ProductInterestsJson` | `jsonb` | `["Dell XPS", ...]` |
| `RecentRatingsJson` | `jsonb` | `[5, 4, 3, ...]` |
| `Summary` | `text?` | LLM ile üretilen özet |
| `AdminNote` | `text?` | Manuel admin notu |
| `TotalSessions` | `int` | |
| `TotalTurns` | `int` | |
| `CreatedAt` | `timestamptz` | |
| `LastInteractionAt` | `timestamptz?` | |
| `LastConsolidatedAt` | `timestamptz?` | Son LLM consolidation zamanı |

---

## `improvement` şeması

### `LessonEntity` → `improvement.lessons`

| Sütun | Tür | Açıklama |
|-------|-----|---------|
| `Id` | `varchar` PK | |
| `Title` | `varchar` | |
| `LessonText` | `text` | "X durumunda Y yap" |
| `Observation` | `text` | Gözlemlenen sorun |
| `SuggestedAgent` | `varchar?` | Hangi agent'a uygulanacak |
| `SourceTraceIdsJson` | `jsonb` | Kaynak trace ID'leri |
| `Status` | `varchar` | `Proposed` / `Approved` / `Rejected` |
| `DecidedBy` | `varchar?` | |
| `DecidedAt` | `timestamptz?` | |
| `DecisionReason` | `text?` | |
| `CreatedAt` | `timestamptz` | |
| `VectorMemoryId` | `varchar?` | VectorStore'daki kayıt ID'si |

---

## `workflow` şeması

### `WorkflowDefinitionEntity` → `workflow.workflow_definitions`

| Sütun | Tür | Açıklama |
|-------|-----|---------|
| `Id` | `varchar` PK | Slugify ile oluşturulur |
| `Name` | `varchar` | |
| `Description` | `text?` | |
| `Version` | `int` | Upsert'te otomatik artırılır |
| `IsActive` | `bool` | |
| `TriggerKeywordsJson` | `jsonb` | Tetikleyici anahtar kelimeler |
| `InputPatternsJson` | `jsonb` | `{"orderId": "\\b(\\d{4,})\\b"}` |
| `StepsJson` | `jsonb` | `List<WorkflowStep>` |
| `CreatedAt` | `timestamptz` | |
| `UpdatedAt` | `timestamptz?` | |
| `UpdatedBy` | `varchar?` | |

---

## `catalog` şeması

### `CategoryEntity` → `catalog.categories`

| Sütun | Tür | Açıklama |
|-------|-----|---------|
| `Id` | `int` PK | |
| `Name` | `varchar` | Kategori adı |

Navigation: `Products` → `List<ProductEntity>`

---

### `CustomerEntity` → `catalog.customers`

| Sütun | Tür | Açıklama |
|-------|-----|---------|
| `Id` | `bigint` identity PK | Otomatik artan |
| `FullName` | `varchar` | Müşteri adı soyadı |
| `Email` | `varchar?` | |
| `Phone` | `varchar?` | |

---

### `ProductEntity` → `catalog.products`

| Sütun | Tür | Açıklama |
|-------|-----|---------|
| `Id` | `int` identity PK | |
| `Name` | `varchar` | Ürün adı |
| `Price` | `decimal` | Fiyat |
| `Stock` | `int` | Stok miktarı |
| `CategoryId` | `int` FK→categories | Kategori referansı |

Navigation: `Category` → `CategoryEntity`, `OrderDetails` → `List<OrderDetailEntity>`

---

### `OrderEntity` → `catalog.orders`

| Sütun | Tür | Açıklama |
|-------|-----|---------|
| `Code` | `bigint` identity PK | Sipariş kodu |
| `CustomerId` | `bigint` FK→customers | |
| `Status` | `varchar` | Sipariş durumu |
| `OrderDate` | `timestamptz` | |
| `CancelledAt` | `timestamptz?` | İptal zamanı |
| `CancelReason` | `text?` | İptal sebebi |
| `ReturnRequestedAt` | `timestamptz?` | İade talep zamanı |
| `ReturnReason` | `text?` | İade sebebi |

Navigation: `Details` → `List<OrderDetailEntity>`

---

### `OrderDetailEntity` → `catalog.order_details`

| Sütun | Tür | Açıklama |
|-------|-----|---------|
| `OrderCode` | `bigint` FK→orders (composite PK) | |
| `ProductId` | `int` FK→products (composite PK) | |
| `Quantity` | `int` | Adet |

Navigation: `Order` → `OrderEntity`, `Product` → `ProductEntity`

---

### `ComplaintEntity` → `catalog.complaints`

| Sütun | Tür | Açıklama |
|-------|-----|---------|
| `Code` | `bigint` PK | ValueGeneratedNever — açıkça atanır |
| `OrderId` | `bigint` FK→orders | İlgili sipariş |
| `CustomerId` | `bigint` FK→customers | İlgili müşteri |
| `Complaint` | `varchar(2048)` | Şikayet metni |
| `Status` | `varchar(64)` | Şikayet durumu |

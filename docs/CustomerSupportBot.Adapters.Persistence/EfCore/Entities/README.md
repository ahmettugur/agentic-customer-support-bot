# CustomerSupportBot.Adapters.Persistence.EfCore.Entities

Bu klasör, PostgreSQL veritabanındaki tabloları temsil eden tüm EF Core varlık (Entity) modellerini bounded context'lere göre gruplanmış olarak barındırır.

## Şema Bazlı Varlık Grupları

### 1. `Catalog` (Şema: `catalog`)
- `CategoryEntity` — Ürün kategorileri (`id`, `category_name`, `description`).
- `ProductEntity` — Ürün kataloğu (`id`, `product_name`, `unit_price`, `units_in_stock`, `category_id`).
- `CustomerEntity` — Müşteri hesapları (`id`, `company_name`, `contact_name`, `phone`, `email`).
- `OrderEntity` — Sipariş başlıkları (`id`, `customer_id`, `order_date`, `status`, `shipping_address`).
- `OrderDetailEntity` — Sipariş kalemleri (`order_id`, `product_id`, `unit_price`, `quantity`, `discount`).
- `ComplaintEntity` — Şikayet kayıtları (`id`, `customer_id`, `order_id`, `complaint_text`, `status`, `created_at`).

### 2. `Chat` (Şema: `chat`)
- `SessionEntity` — Sohbet oturumları (`session_id`, `authenticated_customer_id`, `state_json`, `created_at`).
- `MessageEntity` — Mesaj geçmişi (`id`, `session_id`, `role`, `text`, `timestamp`).
- `ChatSessionModeEntity` — Oturum modu (`session_id`, `mode`, `updated_at`).
- `ChatBridgeMessageEntity` — Köprü mesajları (`id`, `session_id`, `sender`, `text`, `timestamp`).

### 3. `Hitl` (Şema: `hitl`)
- `ApprovalRequestEntity` — HITL onay talepleri (`id`, `session_id`, `tool_name`, `parameters_json`, `status`, `execution_status`, `requested_at`, `decided_at`).
- `EscalationEntity` — İnsan temsilciye eskalasyonlar (`id`, `session_id`, `customer_id`, `reason`, `assigned_agent_id`, `status`).
- `HumanAgentEntity` — Temsilci havuzu (`id`, `name`, `email`, `is_online`, `active_chats_count`).

### 4. `Observability` (Şema: `observability`)
- `ReasoningTraceEntity` — Akıl yürütme trace'leri (`trace_id`, `session_id`, `query`, `reasoning_json`, `planning_json`, `final_response`, `created_at`, `completed_at`).
- `LlmCallUsageEntity` — LLM çağrı token kullanım kayıtları (`id`, `trace_id`, `model`, `provider`, `input_tokens`, `output_tokens`, `cost_usd`, `duration_ms`).

### 5. `Personalization`, `Improvement`, `Knowledge`, `Analytics`, `Auth`
- `CustomerProfileEntity` (Şema: `personalization`) — Müşteri etkileşim profili.
- `LessonEntity` (Şema: `improvement`) — Sistem iyileştirme dersi.
- `KnowledgeArticleEntity` (Şema: `knowledge`) — Bilgi bankası makalesi.
- `RatingEntity` & `SlaEventEntity` (Şema: `analytics`) — Puanlama ve SLA olayları.
- `UserEntity` & `RefreshTokenEntity` (Şema: `auth`) — Kullanıcı ve yenileme token'ı.

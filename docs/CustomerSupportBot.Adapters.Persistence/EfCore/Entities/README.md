# CustomerSupportBot.Adapters.Persistence.EfCore.Entities

Bu klasör, PostgreSQL veritabanındaki tabloları temsil eden tüm EF Core varlık (Entity)
modellerini bounded context'lere (şemalara) göre gruplanmış olarak barındırır. Her entity'nin
kendi `.md` dosyası vardır; her birinin veritabanı eşlemesi (kolon adı, kısıt, index) ise
`../Configurations/` altındaki karşılık gelen dosyada anlatılır.

## Şema Bazlı Varlık Grupları

### 1. `Catalog` (Şema: `catalog`)
- [CategoryEntity](Catalog/CategoryEntity.md) — Ürün kategorileri.
- [ProductEntity](Catalog/ProductEntity.md) — Ürün kataloğu (isim benzersiz, kategoriye FK).
- [CustomerEntity](Catalog/CustomerEntity.md) — Müşteri kayıtları (katalog tarafı).
- [OrderEntity](Catalog/OrderEntity.md) — Sipariş başlıkları (durum, iptal/iade meta verisi).
- [OrderDetailEntity](Catalog/OrderDetailEntity.md) — Sipariş kalemleri (sipariş+ürün composite key).
- [ComplaintEntity](Catalog/ComplaintEntity.md) — Şikayet kayıtları.

### 2. `Chat` (Şema: `chat`)
- [SessionEntity](Chat/SessionEntity.md) — Sohbet oturumları (`SessionState` JSONB).
- [MessageEntity](Chat/MessageEntity.md) — Sıralı user/assistant mesaj geçmişi.
- [ChatSessionModeEntity](Chat/ChatSessionModeEntity.md) — Live Takeover kip kaydı (Bot/Human).
- [ChatBridgeMessageEntity](Chat/ChatBridgeMessageEntity.md) — Live Takeover mesaj geçmişi.

### 3. `Hitl` (Şema: `hitl`)
- [ApprovalRequestEntity](Hitl/ApprovalRequestEntity.md) — HITL onay talebi (talep→karar→yürütme→bildirim yaşam döngüsü).
- [EscalationEntity](Hitl/EscalationEntity.md) — İnsan temsilciye eskalasyon kaydı.
- [HumanAgentEntity](Hitl/HumanAgentEntity.md) — Skills-based routing için temsilci havuzu.

### 4. `Observability` (Şema: `observability`)
- [ReasoningTraceEntity](Observability/ReasoningTraceEntity.md) — Bir turun tüm akıl yürütme kaydı.
- [LlmCallUsageEntity](Observability/LlmCallUsageEntity.md) — Her LLM çağrısının maliyet/performans kaydı.

### 5. `Personalization` (Şema: `personalization`)
- [CustomerProfileEntity](Personalization/CustomerProfileEntity.md) — Müşteri kişiselleştirme profili.

### 6. `Improvement` (Şema: `improvement`)
- [LessonEntity](Improvement/LessonEntity.md) — Self-improving loop'un ürettiği, admin onaylı ders.

### 7. `Knowledge` (Şema: `knowledge`)
- [KnowledgeArticleEntity](Knowledge/KnowledgeArticleEntity.md) — Bilgi bankası makalesi.

### 8. `Analytics` (Şema: `analytics`)
- [RatingEntity](Analytics/RatingEntity.md) — Oturum başına konuşma değerlendirmesi.
- [SlaEventEntity](Analytics/SlaEventEntity.md) — SLA Guardian'ın ürettiği warn/breach olayları.

### 9. `Auth` (Şema: `auth`)
- [UserEntity](Auth/UserEntity.md) — Giriş yapabilen hesap (Admin/Agent/Customer, tek tablo).
- [RefreshTokenEntity](Auth/RefreshTokenEntity.md) — JWT refresh token kaydı (hash'lenmiş, rotasyonlu).

## Bağlantılar

- [../Configurations/README.md](../Configurations/README.md) — Her entity'nin veritabanı eşleme detayları
- [../README.md](../README.md) — EfCore klasörü genel indeksi

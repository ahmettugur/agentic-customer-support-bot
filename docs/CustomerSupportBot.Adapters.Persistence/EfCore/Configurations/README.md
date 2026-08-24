# CustomerSupportBot.Adapters.Persistence.EfCore.Configurations

Bu klasör, `../Entities/` altındaki her EF Core varlığının PostgreSQL tablosuna nasıl eşleneceğini
(kolon adı/tipi/uzunluğu, birincil/yabancı anahtar, index, kısıt) `IEntityTypeConfiguration<T>`
Fluent API'siyle tanımlayan sınıfları barındırır. Her dosya, `../Entities/` altındaki aynı adlı
(sondaki `Entity` yerine `Configuration` ekiyle) dosyayla 1-1 eşleşir ve ona relative link verir.

Tüm bu sınıflar `CustomerSupportDbContext.OnModelCreating` içinde
`ApplyConfigurationsFromAssembly` ile otomatik keşfedilip uygulanır — hiçbiri elle çağrılmaz.

## Şema Bazlı Yapılandırma Grupları

### 1. `Catalog`
- [CategoryConfiguration](Catalog/CategoryConfiguration.md)
- [ProductConfiguration](Catalog/ProductConfiguration.md) — `Name` unique index, `Category` FK (`Restrict`).
- [CustomerConfiguration](Catalog/CustomerConfiguration.md)
- [OrderConfiguration](Catalog/OrderConfiguration.md)
- [OrderDetailConfiguration](Catalog/OrderDetailConfiguration.md) — composite PK, iki farklı silme davranışı.
- [ComplaintConfiguration](Catalog/ComplaintConfiguration.md) — `Code` veritabanınca üretilmez.

### 2. `Chat`
- [SessionConfiguration](Chat/SessionConfiguration.md) — `StateJson` → `jsonb`.
- [MessageConfiguration](Chat/MessageConfiguration.md) — `SessionEntity`'ye gerçek FK, `Cascade`.
- [ChatSessionModeConfiguration](Chat/ChatSessionModeConfiguration.md) — `Mode='Human'` partial index.
- [ChatBridgeMessageConfiguration](Chat/ChatBridgeMessageConfiguration.md)

### 3. `Hitl`
- [ApprovalRequestConfiguration](Hitl/ApprovalRequestConfiguration.md) — 4 farklı sorgu paterni için 4 index.
- [EscalationConfiguration](Hitl/EscalationConfiguration.md) — açık eskalasyonlar için filtreli index.
- [HumanAgentConfiguration](Hitl/HumanAgentConfiguration.md) — aktif temsilciler için filtreli index.

### 4. `Observability`
- [ReasoningTraceConfiguration](Observability/ReasoningTraceConfiguration.md) — çoğu alan `jsonb`.
- [LlmCallUsageConfiguration](Observability/LlmCallUsageConfiguration.md) — `CostUsd` yüksek hassasiyetli `numeric`.

### 5. `Personalization`
- [CustomerProfileConfiguration](Personalization/CustomerProfileConfiguration.md)

### 6. `Improvement`
- [LessonConfiguration](Improvement/LessonConfiguration.md)

### 7. `Knowledge`
- [KnowledgeArticleConfiguration](Knowledge/KnowledgeArticleConfiguration.md)

### 8. `Analytics`
- [RatingConfiguration](Analytics/RatingConfiguration.md) — `stars` `CHECK` kısıtı.
- [SlaEventConfiguration](Analytics/SlaEventConfiguration.md)

### 9. `Auth`
- [UserConfiguration](Auth/UserConfiguration.md) — `LinkedCustomerId` filtreli unique index.
- [RefreshTokenConfiguration](Auth/RefreshTokenConfiguration.md) — `UserEntity`'ye `Cascade` FK.

## Tasarım Notu

Fluent API (Data Annotations yerine) tercih edilmesinin nedeni: Domain/Entity sınıflarının
EF Core'a dair hiçbir attribute taşımaması, altyapı detaylarının tamamen bu ayrı Configuration
sınıflarında toplanması içindir (Separation of Concerns) — bkz. herhangi bir Configuration
dosyasının "Kullanılma Nedeni" bölümü.

## Bağlantılar

- [../Entities/README.md](../Entities/README.md) — Karşılık gelen entity'ler
- [../README.md](../README.md) — EfCore klasörü genel indeksi

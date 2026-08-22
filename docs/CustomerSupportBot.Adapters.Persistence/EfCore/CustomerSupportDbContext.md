# CustomerSupportDbContext

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/EfCore/CustomerSupportDbContext.cs`
- **Tür:** `public sealed class : DbContext`
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.EfCore`

## Ne işe yarar?

`CustomerSupportDbContext`, EF Core 10 ve Npgsql PostgreSQL sağlayıcısı üzerinden projenin tüm ilişkisel veritabanı işlemlerini yürüten merkezi `DbContext` sınıfıdır.

## Hangi amaçla kullanılır`?

- `chat`, `hitl`, `catalog`, `observability`, `knowledge`, `auth`, `personalization`, `improvement` ve `analytics` şemalarındaki tüm tabloları temsil eden `DbSet<T>` koleksiyonlarına erişim sağlamak.
- `ApplyConfigurationsFromAssembly` ile `Configurations/` altındaki tüm `IEntityTypeConfiguration<T>` yapılandırmalarını (tablo adı, şema, foreign key, index, JSONB sütun dönüşümleri) otomatik olarak uygulamak.

## Constructor ve Başlatma Mantığı

```csharp
public CustomerSupportDbContext(DbContextOptions<CustomerSupportDbContext> options)
    : base(options)
```

### Constructor İçerisinde Yapılan İşler:
- `options` (`DbContextOptions<CustomerSupportDbContext>`): PostgreSQL bağlantı dizesi, Npgsql eklentileri ve havuz ayarları temel `DbContext` sınıfına aktarılır.

## Metotlar ve İç Çalışma Mantıkları

### 1. `OnModelCreating`
```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
```
- **Ne işe yarar?:** Veritabanı modeli oluşturulurken tüm yapılandırma sınıflarını yükler.
- **İç Mantığı:** `modelBuilder.ApplyConfigurationsFromAssembly(typeof(CustomerSupportDbContext).Assembly)` çağrılarak `Configurations/` altındaki onlarca entity configuration sınıfı tek satırda taranıp modele eklenir.

## Tanımlı `DbSet` Tablo Kümeleri

| Şema | DbSet Özelliği | Varlık Tipi (Entity) | Açıklama |
|---|---|---|---|
| `chat` | `Sessions`, `Messages`, `ChatSessionModes`, `ChatBridgeMessages` | `SessionEntity`, `MessageEntity`, ... | Sohbet oturumları, mesaj geçmişi ve köprü mesajları. |
| `hitl` | `Approvals`, `Escalations`, `HumanAgents` | `ApprovalRequestEntity`, ... | Onay kuyruğu, eskalasyonlar ve insan temsilciler. |
| `observability`| `ReasoningTraces`, `LlmCallUsages` | `ReasoningTraceEntity`, ... | Akıl yürütme trace'leri ve LLM token kullanım kayıtları. |
| `catalog` | `Categories`, `Customers`, `Orders`, `OrderDetails`, `Products`, `Complaints` | `ProductEntity`, `OrderEntity`, ... | E-ticaret ürün, müşteri, sipariş ve şikayet kayıtları. |
| `auth` | `Users`, `RefreshTokens` | `UserEntity`, `RefreshTokenEntity` | Kullanıcı kimlikleri, roller ve JWT yenileme token'ları. |
| `knowledge` | `KnowledgeArticles` | `KnowledgeArticleEntity` | Şirket bilgi tabanı makaleleri. |
| `improvement` | `Lessons` | `LessonEntity` | Kendi kendini iyileştirme (self-improvement) dersleri. |
| `analytics` | `Ratings`, `SlaEvents` | `RatingEntity`, `SlaEventEntity` | Müşteri puanlamaları ve SLA olay kayıtları. |
| `personalization`| `CustomerProfiles` | `CustomerProfileEntity` | Müşteri etkileşim profilleri ve özetleri. |

## Bağımlılıklar

- `Microsoft.EntityFrameworkCore.DbContext`
- `Npgsql.EntityFrameworkCore.PostgreSQL`
- [Schemas](Schemas.md)

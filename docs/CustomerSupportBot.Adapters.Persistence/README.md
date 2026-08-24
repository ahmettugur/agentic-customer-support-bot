# CustomerSupportBot.Adapters.Persistence

Bu klasör, hexagonal mimaride **Driven Adapter (Çıkış Adaptörü)** rolünü üstlenen; ilişkisel veritabanı (PostgreSQL + EF Core 10), dosya sistemi (Markdown Prompt & KnowledgeBase) ve testler/fallback için bellek içi (InMemory) ambarları yöneten kalıcılık adaptörüdür.

## Dizin Yapısı

- [EfCore/](EfCore/README.md) — EF Core 10 DbContext, şemalar, varlık modelleri (Entities), arkaplan süpürme ve başlangıç tohumlayıcıları:
  - [CustomerSupportDbContext](EfCore/CustomerSupportDbContext.md) — Npgsql tabanlı PostgreSQL DbContext.
  - [PersistenceServiceCollectionExtensions](EfCore/PersistenceServiceCollectionExtensions.md) — `AddCustomerSupportPersistence` DI kaydı.
  - [DesignTimeDbContextFactory](EfCore/DesignTimeDbContextFactory.md) — `dotnet ef` için design-time fabrikası.
  - [PersistenceOptions](EfCore/PersistenceOptions.md) — `Persistence:Provider` konfigürasyonu.
  - [Schemas](EfCore/Schemas.md) — Çoklu şema (`chat`, `hitl`, `catalog`, `auth`, `observability` vb.) sabitleri.
  - [PersistenceHydrator](EfCore/PersistenceHydrator.md) — Yeniden başlatmalarda yarım kalan in-flight trace'leri toparlayan başlangıç servisi.
  - [StaleApprovalSweepService](EfCore/StaleApprovalSweepService.md) — Bloklamayan modelde süresi geçen HITL onaylarını otomatik reddeden periyodik süpürge.
  - [DemoDataSeeder](EfCore/DemoDataSeeder.md) & [NorthwindSeedData](EfCore/NorthwindSeedData.md) — Başlangıç demo verisi tohumlayıcıları.
  - [EfUserAuthRepository](EfCore/Auth/EfUserAuthRepository.md) & [EfRefreshTokenRepository](EfCore/Auth/EfRefreshTokenRepository.md) — Kullanıcı ve JWT refresh token ambarları.
  - [Entities/](EfCore/Entities/README.md) — PostgreSQL tablolarını temsil eden EF Core entity modelleri.
- [Migrations/](Migrations/README.md) — EF Core CLI tarafından otomatik üretilen şema migration'ları; repo'nun "squashed migration" konvansiyonu burada açıklanır.
- [Postgres/](Postgres/README.md) — PostgreSQL + IDbContextFactory tabanlı kalıcı depo implementasyonları (her sınıf kendi .md dosyasında; tam liste ve hibrit cache mimarisi notu için bkz. [Postgres/README.md](Postgres/README.md)).
- [InMemory/](InMemory/README.md) — Geliştirme, birim/entegrasyon testleri ve veritabanı yokluğunda devreye giren bellek içi fallback depoları (production DI'da kayıtlı değildir, sadece test projelerinde kullanılır).
- [Auth/](Auth/README.md) — Parola hash'leme (`BCryptPasswordHasher`) ve JWT üretimi (`JwtAccessTokenProvider`); `EfCore/Auth/` (repository'ler) ile karıştırılmamalı.
- [FileSystem/](FileSystem/README.md) — Markdown prompt şablonları (`FileSystemPromptRepository`, `PromptOptions`) ve dosya sistemi bilgi bankası kaynakları (`FileSystemKnowledgeBaseSource`).
- [HealthChecks/](HealthChecks/PostgresHealthCheck.md) — Npgsql bağlantı sağlık kontrolcüsü (`PostgresHealthCheck`).
- [DependencyInjection/](DependencyInjection/PersistenceAdapterServiceCollectionExtensions.md) — `AddPersistenceAdapters` IoC kayıt uzantısı.
- [ExceptionTranslator](ExceptionTranslator.md) — Npgsql ve EF Core istisnalarını DomainException'a çevirici.

## Mimari Rolü ve Yetenekleri

- **Çoklu PostgreSQL Şeması:** Veritabanı tabloları mantıksal bounded context'lere göre (`catalog`, `chat`, `hitl`, `observability`, `knowledge`, `auth` vb.) ayrılmıştır.
- **Hibrit Cache + DB Mimarisi:** Kritik onay ve sohbet akışlarında (ör. `PostgresApprovalQueue`), yüksek okuma performansı için yerel `ConcurrentDictionary` ve pod'lar arası senkronizasyon için Redis pub/sub ile desteklenir.
- **Güvenli Stok Düşümü:** [`StockDeduction.TryDeduct`](Postgres/StockDeduction.md) ile eşzamanlı siparişlerde eksiye düşmeyi engelleyen atomik SQL `UPDATE products SET stock = stock - @qty WHERE name = @name AND stock >= @qty` kontrolü (çağıranın transaction'ı içinde, senkron).

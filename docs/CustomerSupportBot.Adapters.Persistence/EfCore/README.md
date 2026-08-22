# CustomerSupportBot.Adapters.Persistence.EfCore

Bu klasör, Entity Framework Core 10 ve Npgsql tabanlı PostgreSQL veritabanı bağlamını (DbContext), şema tanımlarını, varlık modellerini ve arkaplan yaşam döngüsü servislerini barındırır.

## Dosyalar

- [CustomerSupportDbContext](CustomerSupportDbContext.md) — EF Core 10 DbContext; tüm `DbSet` tanımlarını ve `ApplyConfigurationsFromAssembly` yapılandırmasını içerir.
- [Schemas](Schemas.md) — Veritabanı şema adları sabitleri (`chat`, `hitl`, `catalog`, `observability`, `knowledge`, `auth` vb.).
- [PersistenceHydrator](PersistenceHydrator.md) — Uygulama başlangıcında yarım kalmış in-flight trace'leri toparlayan `IHostedService`.
- [StaleApprovalSweepService](StaleApprovalSweepService.md) — Süresi dolan (`StalePendingHours`) bekleyen HITL onaylarını otomatik reddeden `BackgroundService`.
- [DemoDataSeeder](DemoDataSeeder.md) — Uygulama başlangıcında varsayılan kullanıcıları, personelleri ve demo verilerini tohumlayan servis.
- [NorthwindSeedData](NorthwindSeedData.md) — Northwind e-ticaret demo kataloğu (kategoriler, ürünler, müşteriler, siparişler).
- [EfUserAuthRepository](Auth/EfUserAuthRepository.md) — [IUserAuthRepository](../../CustomerSupportBot.Application/Ports/Outbound/Auth/IUserAuthRepository.md) portunu EF Core ile uygulayan kimlik doğrulama ambarı.
- [EfRefreshTokenRepository](Auth/EfRefreshTokenRepository.md) — [IRefreshTokenRepository](../../CustomerSupportBot.Application/Ports/Outbound/Auth/IRefreshTokenRepository.md) portunu EF Core ile uygulayan JWT refresh token ambarı.
- [Entities/](Entities/README.md) — PostgreSQL tablolarını temsil eden EF Core entity sınıfları.

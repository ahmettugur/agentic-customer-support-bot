# PersistenceAdapterServiceCollectionExtensions

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/DependencyInjection/PersistenceAdapterServiceCollectionExtensions.cs`
- **Tür:** `public static class`
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.DependencyInjection`

## Ne işe yarar?

`PersistenceAdapterServiceCollectionExtensions`, EF Core 10 DbContextFactory, PostgreSQL depoları, kimlik doğrulama servisleri, dosya sistemi prompt yöneticisi ve arkaplan yaşam döngüsü servislerini (`PersistenceHydrator`, `StaleApprovalSweepService`) IoC konteynerine kaydeden ana DI uzantısıdır.

## Hangi amaçla kullanılır`?

- Composition Root (`CustomerSupportBot.Api`) tarafında `services.AddPersistenceAdapters(configuration)` çağrısıyla tüm kalıcılık altyapısını başlatmak.
- `PersistenceOptions.Provider` (PostgreSQL) ayarına göre doğru adaptörleri IoC konteynerine bağlamak.

## Metotlar ve İç Çalışma Mantıkları

### 1. `AddPersistenceAdapters`
```csharp
public static IServiceCollection AddPersistenceAdapters(
    this IServiceCollection services,
    IConfiguration configuration)
```
- **Ne işe yarar?:** Kalıcılık servislerini ve EF Core DbContext havuzunu kaydeder.
- **İç Mantığı:**
  1. `AddPooledDbContextFactory<CustomerSupportDbContext>` ile Npgsql bağlantısını havuzlar.
  2. Tüm Repository, Store ve Sink portlarını (Postgres implementasyonları ile) kaydeder.
  3. `FileSystemPromptRepository` (`IPromptRepository`) ve `FileSystemKnowledgeBaseSource` servislerini bağlar.
  4. `PersistenceHydrator` ve `StaleApprovalSweepService` arkaplan servislerini ekler.

## Bağımlılıklar

- [CustomerSupportDbContext](../EfCore/CustomerSupportDbContext.md)
- [FileSystemPromptRepository](../FileSystem/FileSystemPromptRepository.md)
- [PersistenceHydrator](../EfCore/PersistenceHydrator.md)
- [StaleApprovalSweepService](../EfCore/StaleApprovalSweepService.md)

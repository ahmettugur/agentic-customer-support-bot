# PersistenceAdapterServiceCollectionExtensions

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/DependencyInjection/PersistenceAdapterServiceCollectionExtensions.cs`
- **Tür:** `public static class`
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.DependencyInjection`

## Ne işe yarar?

`PersistenceAdapterServiceCollectionExtensions`, EF Core 10 DbContextFactory, PostgreSQL depoları, kimlik doğrulama servisleri, dosya sistemi prompt yöneticisi ve arkaplan yaşam döngüsü servislerini (`PersistenceHydrator`, `StaleApprovalSweepService`) IoC konteynerine kaydeden ana DI uzantısıdır.

## Hangi amaçla kullanılır`?

- Composition Root (`CustomerSupportBot.Api`) tarafında `services.AddPersistenceAdapters(configuration)` çağrısıyla tüm kalıcılık altyapısını başlatmak.
- `PersistenceOptions.Provider` (PostgreSQL) ayarına göre doğru adaptörleri IoC konteynerine bağlamak.

## Sorumlulukları

- `ConnectionStrings:PostgreSQL`'in tanımlı olduğunu doğrulamak (yoksa açılışta `InvalidOperationException` — fail-fast).
- `PersistenceOptions`'ı appsettings'ten bağlamak.
- `PersistenceServiceCollectionExtensions.AddCustomerSupportPersistence` (EfCore katmanı) çağrısıyla DbContext havuzunu ve şema altyapısını kaydetmek.
- Üç arkaplan `IHostedService`'i eklemek: `PersistenceHydrator` (restart sonrası yarım kalan trace'leri toparlar), `DemoDataSeeder` (başlangıç demo verisi), `StaleApprovalSweepService` (süresi geçen HITL onaylarını periyodik reddeder).
- Auth repository'lerini (`IUserAuthRepository`, `IRefreshTokenRepository`) EF Core implementasyonlarına bağlamak.
- **Production'da kullanılan** tüm Outbound port implementasyonlarını (hepsi `Postgres*` sınıfları — `IReasoningTraceStore`, `IApprovalQueue`, `IEscalationSink`, `IChatModeRegistry`, `IChatBridge`, `ISessionManager`, `IRatingStore`, `IHumanAgentRegistry`, `ICustomerProfileStore`, `ILessonStore`, `ISlaEventSink`, `IKnowledgeArticleStore`, dört repository) tekil (`AddSingleton`) olarak kaydetmek.
- `FileSystemPromptRepository` (`IPromptRepository`) ve `FileSystemKnowledgeBaseSource` (`IKnowledgeBaseSource`) dosya sistemi adaptörlerini bağlamak.

**Üstlenmediği:** `InMemory/` klasöründeki hiçbir sınıf burada kayıtlı DEĞİLDİR — production DI her zaman Postgres implementasyonlarını kullanır; `InMemory*` sınıflar sadece test projelerinin kendi `WebApplicationFactory`/DI kurulumlarında devreye girer.

## Diğer Katman ve Bileşenlerle İlişkileri

- `CustomerSupportBot.Api` (composition root) tarafından `Program.cs`'te `services.AddPersistenceAdapters(configuration)` ile çağrılır.
- `PersistenceServiceCollectionExtensions` (EfCore katmanı) ile birlikte çalışır — DbContext/şema kaydı orada, port implementasyonu eşlemesi burada.

## Kullanılma Nedeni ve Tasarım Yaklaşımı

Tüm kalıcılık DI kayıtlarının tek bir extension metodunda toplanması, composition root'un (`Program.cs`) katman detaylarından habersiz, sadece `AddPersistenceAdapters(configuration)` gibi tek satırlık çağrılarla ("thin composition root" deseni) kalması içindir — hangi port'un hangi implementasyona bağlandığı bilgisi Adapters katmanının kendi sorumluluğundadır, Api katmanı bunu bilmek zorunda değildir.

## Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `AddPersistenceAdapters(services, configuration)` | Tüm Postgres/EfCore/FileSystem port implementasyonlarını ve üç arkaplan servisini IoC konteynerine kaydeder; `ConnectionStrings:PostgreSQL` yoksa fırlatır. |

## Bağımlılıklar

- [CustomerSupportDbContext](../EfCore/CustomerSupportDbContext.md)
- [FileSystemPromptRepository](../FileSystem/FileSystemPromptRepository.md)
- [PersistenceHydrator](../EfCore/PersistenceHydrator.md)
- [StaleApprovalSweepService](../EfCore/StaleApprovalSweepService.md)

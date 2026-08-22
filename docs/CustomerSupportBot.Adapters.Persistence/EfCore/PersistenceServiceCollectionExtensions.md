# PersistenceServiceCollectionExtensions

**Dosya:** `EfCore/PersistenceServiceCollectionExtensions.cs`
**Namespace:** `CustomerSupportBot.Adapters.Persistence.EfCore`
**Tip:** `static class` (extension method konteyneri)

## 1. Ne İşe Yarar

`CustomerSupportDbContext` ve `IDbContextFactory<CustomerSupportDbContext>`'i DI konteynerine kaydeden tek DI uzantı metodunu barındırır.

## 2. Hangi Amaçla Kullanılır

`Program.cs`/kompozisyon kökü tarafından uygulama başlangıcında bir kez çağrılır (`AddCustomerSupportPersistence`).

## 3. Sorumlulukları

- Üstlendiği: Npgsql provider kaydı, migration history tablosu adlandırması, otomatik yeniden deneme (`EnableRetryOnFailure`) yapılandırması, dev-ortamı model-uyarı bastırma.
- Üstlenmediği: bağlantı dizesinin KENDİSİNİN nereden geldiği (çağıran taraftan parametre olarak alınır — appsettings/ortam değişkeni okuma bu sınıfın işi değildir).

## 4. İlişkiler

- [`CustomerSupportDbContext`](CustomerSupportDbContext.md)'i `AddDbContextFactory` ile kaydeder.
- `Program.cs` (Api katmanı) tarafından çağrılır.

## 5. Tasarım Yaklaşımı

`IDbContextFactory<T>` (doğrudan `DbContext` DI'ı değil) kullanılır çünkü bu projedeki çoğu Postgres adaptörü **Singleton** ömründedir (örn. `PostgresApprovalQueue`, `PostgresSessionManager`) — `DbContext`'in kendisi Scoped/thread-safe olmadığından, Singleton bir sınıfın her metod çağrısında `_dbFactory.CreateDbContext()` ile taze, kısa ömürlü bir context açıp kapatması gerekir.

`EnableRetryOnFailure(maxRetryCount: 3)` açık olduğundan, aktif execution strategy `NpgsqlRetryingExecutionStrategy`'dir — bu, elle açılan (`Database.BeginTransaction()`) transaction'ları reddeder; bu yüzden [`OrderRepository`](../Postgres/OrderRepository.md) gibi transaction kullanan sınıflar `CreateExecutionStrategy().Execute(...)` sarmalayıcısını kullanmak ZORUNDADIR.

`PendingModelChangesWarning` bilinçli olarak bastırılır: geliştirme akışında model snapshot'ı ile gerçek şema arasındaki küçük farklar (henüz migration'a dökülmemiş) uygulamanın açılmasını engellemesin diye — gerçek şema uyuşmazlıkları zaten migration dosyalarında görülür.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `static IServiceCollection AddCustomerSupportPersistence(this IServiceCollection services, string connectionString)` | `CustomerSupportDbContext` + `IDbContextFactory<CustomerSupportDbContext>`'i Npgsql provider ile kaydeder. |

## 7. Bağımlılıklar

Yok (bu sınıfın kendisi DI'a bir şey enjekte edilmez, yalnızca kayıt yapar).

## Bağlantılar

- [CustomerSupportDbContext](CustomerSupportDbContext.md)
- [OrderRepository](../Postgres/OrderRepository.md) — retry-uyumlu transaction kullanımı

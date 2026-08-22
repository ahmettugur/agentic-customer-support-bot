# DesignTimeDbContextFactory

**Dosya:** `EfCore/DesignTimeDbContextFactory.cs`
**Namespace:** `CustomerSupportBot.Adapters.Persistence.EfCore`
**Uyguladığı Arayüz:** `IDesignTimeDbContextFactory<CustomerSupportDbContext>` (EF Core Design paketi)

## 1. Ne İşe Yarar

`dotnet ef migrations add`/`dotnet ef database update` gibi **design-time** (derleme zamanı araç) komutlarının çalışırken uygulamayı gerçekten ayağa kaldırmadan bir `CustomerSupportDbContext` örneği alabilmesini sağlayan fabrika.

## 2. Hangi Amaçla Kullanılır

EF Core CLI araçları, projede `IDesignTimeDbContextFactory<T>` implementasyonu bulduğunda uygulamanın `Program.cs`'ini/DI konteynerini hiç çalıştırmadan doğrudan bunu çağırır. Yeni migration üretilirken veya migration'lar veritabanına uygulanırken devreye girer.

## 3. Sorumlulukları

- Üstlendiği: `appsettings.json`/`appsettings.Development.json`/ortam değişkenlerinden bağlantı dizesini okuyup design-time için minimal bir `DbContextOptions` kurmak.
- Üstlenmediği: runtime DI kaydı (bu [`PersistenceServiceCollectionExtensions`](PersistenceServiceCollectionExtensions.md)'ın işi — iki sınıf birbirinden bağımsız, biri yalnızca `dotnet ef` için, diğeri yalnızca çalışan uygulama için).

## 4. İlişkiler

- Yalnızca `dotnet ef` CLI aracı tarafından reflection ile bulunup çağrılır — kod içinden hiçbir yerden referans verilmez.
- [`CustomerSupportDbContext`](CustomerSupportDbContext.md)'i örnekler.

## 5. Tasarım Yaklaşımı

> 🐞 **Neden Postgres bağlantı dizesi HER ZAMAN doğrudan `ConnectionStrings:PostgreSQL`'den okunuyor, `Persistence:Provider` ayarına bakılmadan:** Dosya başındaki yorum bunun kasıtlı olduğunu belirtir — "runtime'da `Persistence:Provider` InMemory iken bile migration üretebilmek için". Yani migration dosyaları, uygulamanın o an hangi sağlayıcıyla çalıştığından BAĞIMSIZ olarak her zaman gerçek Postgres şemasına göre üretilmelidir; aksi hâlde geliştirme ortamında InMemory ile çalışırken migration eklemeye çalışmak (yanlışlıkla) başarısız olur veya yanlış bir şema üretir. *(Not: bu dokümantasyon denetimi sırasında `PersistenceOptions.PersistenceProvider` enum'unun şu an yalnızca `Postgres` değerini içerdiği, `InMemory` seçeneğinin koddan kaldırılmış olabileceği görüldü — bkz. [PersistenceOptions](PersistenceOptions.md); bu yorum tarihsel bir kalıntı olabilir, üretim davranışını etkilemez çünkü sonuç zaten her koşulda Postgres'tir.)*

Bağlantı dizesi bulunamazsa (`ConnectionStrings:PostgreSQL` eksikse) açıklayıcı bir `InvalidOperationException` fırlatılır — sessiz `null`/varsayılan yerine geliştiriciye "bunu ayarlaman gerekiyor" net hatası verir.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `CustomerSupportDbContext CreateDbContext(string[] args)` | `appsettings*.json` + ortam değişkenlerinden config okur, `ConnectionStrings:PostgreSQL`'i alır (yoksa hata fırlatır), Npgsql provider ile `DbContextOptions` kurup context döner. |

## 7. Bağımlılıklar

Yok (constructor injection yok — `IDesignTimeDbContextFactory` arayüzü parametresiz örnekleme gerektirir, tüm bağımlılıklar metot içinde elle kurulur).

## Bağlantılar

- [CustomerSupportDbContext](CustomerSupportDbContext.md)
- [PersistenceServiceCollectionExtensions](PersistenceServiceCollectionExtensions.md) — runtime karşılığı
- [PersistenceOptions](PersistenceOptions.md)

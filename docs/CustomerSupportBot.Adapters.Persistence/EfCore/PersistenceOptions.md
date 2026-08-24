# PersistenceOptions / PersistenceProvider

**Dosya:** `EfCore/PersistenceOptions.cs`
**Namespace:** `CustomerSupportBot.Adapters.Persistence.EfCore`
**Tipler:** `PersistenceOptions` (sealed class), `PersistenceProvider` (enum)

## 1. Ne İşe Yarar

`appsettings.json`'daki `"Persistence"` bölümünü (`Persistence:Provider`) tipli bir konfigürasyon nesnesine bağlayan `IOptions<T>` sınıfı. `PersistenceProvider` enum'u, hangi kalıcılık sağlayıcısının kullanılacağını ifade eder.

## 2. Hangi Amaçla Kullanılır

`PersistenceAdapterServiceCollectionExtensions` (`DependencyInjection/`) başlangıçta `configuration.GetSection(PersistenceOptions.SectionName)` ile bu tipi DI'a `IOptions<PersistenceOptions>` olarak bağlar; `Api` katmanındaki `WebApplicationExtensions`/`HealthCheckExtensions` bu ayarı okuyarak sağlayıcıya özgü davranış (örn. hangi health check'in kayıtlı olacağı) seçer.

## 3. Sorumlulukları

- Üstlendiği: `Persistence:Provider` ayarının tipe güvenli okunması.
- Üstlenmediği: sağlayıcıya göre farklı DI kayıtlarının YAPILMASI (bu, bu options'ı okuyan `Api`/`DependencyInjection` katmanındaki extension metotların işi).

## 4. İlişkiler

- `PersistenceAdapterServiceCollectionExtensions` tarafından `services.Configure<PersistenceOptions>(...)` ile bağlanır.
- `CustomerSupportBot.Api/Extensions/WebApplicationExtensions.cs` ve `HealthCheckExtensions.cs` tarafından okunur.

## 5. Tasarım Yaklaşımı

> 🐞 **`PersistenceProvider` enum'u şu an yalnızca `Postgres` değerini içeriyor:** [`DesignTimeDbContextFactory`](DesignTimeDbContextFactory.md)'deki bir yorum hâlâ "`Persistence:Provider` InMemory iken bile..." ifadesini kullanıyor — bu, geçmişte bir `InMemory` sağlayıcı seçeneğinin var olduğuna ama koddan kaldırıldığına işaret ediyor (muhtemelen üretim/geliştirme ortamlarının ikisi de artık gerçek Postgres kullanıyor, InMemory yalnızca testlerde `TestDbContextFactory`/`PostgresCatalogFixture` üzerinden ayrıca ele alınıyor). Enum tek değerli olduğu için `PersistenceProvider.Provider` alanı şu an fiilen sabit bir değer taşıyor; kaldırılıp kaldırılmayacağına bu dokümantasyon görevi kapsamında karar verilmedi.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `const string PersistenceOptions.SectionName = "Persistence"` | `appsettings.json`'daki bölüm anahtarı. |
| `PersistenceProvider PersistenceOptions.Provider { get; set; }` | Varsayılan `PersistenceProvider.Postgres`. |
| `enum PersistenceProvider { Postgres }` | Tek değerli — bkz. yukarıdaki 🐞 notu. |

## 7. Bağımlılıklar

Yok — saf bir POCO options sınıfı.

## Bağlantılar

- [DesignTimeDbContextFactory](DesignTimeDbContextFactory.md)
- [PersistenceServiceCollectionExtensions](PersistenceServiceCollectionExtensions.md)

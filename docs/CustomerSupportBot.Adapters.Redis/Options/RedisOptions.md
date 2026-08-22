# RedisOptions

- **Kaynak:** `CustomerSupportBot.Adapters.Redis/RedisOptions.cs`
- **Tür:** `public sealed class`
- **Namespace:** `CustomerSupportBot.Adapters.Redis`

## Ne işe yarar?

`RedisOptions`, `appsettings.json` içerisindeki `"Redis"` bölümünü strongly-typed C# modeline bağlayan seçenek (options) sınıfıdır. ASP.NET Core'un `IOptions<T>` pattern'ine uyar.

## Hangi amaçla kullanılır?

- Redis bağlantı dizesini (`ConnectionString`) yapılandırmak.
- Bağlantı dizesi `Redis:ConnectionString` altında yoksa, `ConnectionStrings:Redis` üzerinden fallback yapılmasını desteklemek (bkz. [RedisAdapterServiceCollectionExtensions](../DependencyInjection/RedisAdapterServiceCollectionExtensions.md)).

## Sorumlulukları

- **Üstlendiği:** Sadece appsettings'ten okunan ham değerleri tip-güvenli bir C# nesnesine taşımak (POCO). İş mantığı içermez.
- **Üstlenmediği:** Değerlerin doğrulanması (validation) veya varsayılan değerlerin uygulanması — `ConnectionString` boşsa hata fırlatma sorumluluğu bu sınıfta değil, [`RedisAdapterServiceCollectionExtensions.AddRedisAdapters`](../DependencyInjection/RedisAdapterServiceCollectionExtensions.md)'da.

## Diğer Katman ve Bileşenlerle İlişkileri

- `RedisAdapterServiceCollectionExtensions.AddRedisAdapters`, `configuration.GetSection(RedisOptions.SectionName)` ile bu sınıfı hem `IOptions<RedisOptions>` olarak DI'a kaydeder hem de bağlantı dizesini senkron çözümlemek için anında `section.Get<RedisOptions>()` çağırır.

## Kullanılma Nedeni ve Tasarım Yaklaşımı

Konfigürasyonu doğrudan `IConfiguration["Redis:ConnectionString"]` string anahtarlarıyla okumak yerine strongly-typed bir sınıfa bağlamak, yazım hatalarını derleme zamanında yakalamayı ve appsettings.json şemasını kod üzerinden belgelenebilir kılmayı sağlar (options pattern).

> 🐞 **Dikkat — `KeyPrefix`, `DefaultLockTimeoutSeconds`, `LockExpirySeconds` şu an KULLANILMIYOR (ölü konfigürasyon):**
> Bu üç alan tanımlanmış ve appsettings.json'da ayarlanabilir olsa da, kod tabanında hiçbir yerde okunmuyorlar (`grep` ile doğrulandı — sadece bu dosyada tanımlılar). Gerçek davranış:
> - Kilit anahtarına bir prefix eklenmiyor (`RedisDistributedLockAdapter`, `resourceKey`'i doğrudan, prefix'siz kullanıyor).
> - Varsayılan kilit zaman aşımı, [`RedisDistributedLockAdapter`](../Locking/RedisDistributedLockAdapter.md) içinde kodda sabit `TimeSpan.FromSeconds(5)` olarak tanımlı — bu options'tan gelmiyor.
> - Kilit kirası (lease) süresi `Medallion.Threading.Redis`'in kendi varsayılanına bırakılmış.
>
> Bu alanları appsettings.json'da değiştirmek **hiçbir gözlemlenebilir etki yaratmaz**. Bu sınıfı kullanan/genişleten biri önce bu notu görmeli — aksi halde "neden zaman aşımını değiştirdim ama davranış aynı kaldı" şaşkınlığı yaşanır.

## Alanlar ve Özellikler

| Alan | Tür | Varsayılan | Açıklama | Gerçekten kullanılıyor mu? |
|---|---|---|---|---|
| `SectionName` | `const string` | `"Redis"` | Konfigürasyon bölüm adı (`configuration.GetSection` anahtarı). | ✅ Evet |
| `ConnectionString` | `string?` | `null` | Redis bağlantı dizesi (Örn: `localhost:6379,password=...`). Boşsa `ConnectionStrings:Redis`'e düşülür. | ✅ Evet |
| `KeyPrefix` | `string` | `"csbot"` | Kilit anahtarı öneki — çok kiracılı (multi-tenant) ortamlarda çakışmayı önlemek için tasarlanmış. | ❌ Hayır — yukarıdaki uyarıya bakın |
| `DefaultLockTimeoutSeconds` | `int` | `10` | Varsayılan kilit edinme zaman aşımı (saniye). | ❌ Hayır — yukarıdaki uyarıya bakın |
| `LockExpirySeconds` | `int` | `30` | Kilit kirası süresi (saniye) — bu süre sonunda kilit otomatik serbest kalır. | ❌ Hayır — yukarıdaki uyarıya bakın |

## Bağımlılıklar

Yok — saf bir konfigürasyon POCO'sudur, başka hiçbir tipe bağımlı değildir.

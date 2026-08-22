# RedisOptions

- **Kaynak:** `CustomerSupportBot.Adapters.Redis/RedisOptions.cs`
- **Tür:** `public sealed class`
- **Namespace:** `CustomerSupportBot.Adapters.Redis`

## Ne işe yarar?

`RedisOptions`, `appsettings.json` içerisindeki `"Redis"` bölümünü strongly-typed C# modeline bağlayan seçenek sınıfıdır.

## Hangi amaçla kullanılır`?

- Redis bağlantı dizesini (`ConnectionString`) yapılandırmak.
- Bağlantı dizesi `Redis:ConnectionString` altında yoksa, `ConnectionStrings:Redis` üzerinden fallback yapılmasını desteklemek.

## Alanlar ve Özellikler

| Alan | Tür | Varsayılan | Açıklama |
|---|---|---|---|
| `SectionName` | `const string` | `"Redis"` | Konfigürasyon bölüm adı. |
| `ConnectionString` | `string?` | `null` | Redis bağlantı dizesi (Örn: `localhost:6379,password=...`). |

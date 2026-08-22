# ConfiguredA2ASubjectAuthorizer

**Dosya:** `Services/A2A/ConfiguredA2ASubjectAuthorizer.cs`
**Tür:** `public sealed class : IA2ASubjectAuthorizer` + yapılandırma tipleri (`A2AOptions`, `A2APartnerOptions`)
**Namespace:** `CustomerSupportBot.Application.Services.A2A`

## 1. Ne İşe Yarar

`IA2ASubjectAuthorizer` port'unun, `appsettings.json`'daki `A2A:Partners` listesine dayalı
implementasyonu: "bu partner, bu müşteri adına hareket edebilir mi?" sorusunu statik bir
yapılandırma listesine bakarak cevaplar.

## 2. Hangi Amaçla Kullanılır

[`A2ATokenExchangeService.ExchangeAsync`](A2ATokenExchangeService.md), özne token'ı üretmeden
önce `CanActForCustomerAsync`'i çağırır. Bu sınıf, partner kimliğinin `A2AOptions.Partners`
listesinde tanımlı olup olmadığına ve o partnerin `AllowedCustomerIds` listesinin istenen
müşteriyi (veya joker `"*"`) içerip içermediğine bakar.

## 3. Sorumlulukları

- **Üstlendiği:** Statik/yapılandırma tabanlı yetki kontrolü, red durumlarını loglamak.
- **Üstlenmediği:** Dinamik/veritabanı tabanlı partner-müşteri ilişkisi (bkz. §5), token üretimi.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IA2ASubjectAuthorizer` port'unu implemente eder (port: `Ports/Outbound/A2A/IA2ASubjectAuthorizer.cs`).
- [`A2ATokenExchangeService`](A2ATokenExchangeService.md) tarafından inject edilip çağrılır.
- `A2AOptions`, Api katmanındaki A2A endpoint'lerinin rate-limit/gövde-boyutu/keşif ayarlarını da taşır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 🐞 **Varsayılan davranış REDDETMEKTİR.** Partner tanımlı değilse, hiç yapılandırma yoksa veya
> müşteri o partnerin izin listesinde yoksa sonuç kesin olarak `false`'tur. Bu bilinçli bir
> tasarım kararı: A2A kanalı dış sistemlere açık bir kapı ve bu kapı yanlış açıldığında bedeli
> başka bir müşterinin sipariş/şikayet geçmişinin sızmasıdır. "Yapılandırmayı unuttum" durumunun
> sonucu **sızıntı değil, çalışmama** olmalıdır — fail-closed, fail-open değil.

`"*"` joker değeri (`AllowedCustomerIds` içinde) bir partnerin **tüm** müşteriler adına hareket
edebileceği anlamına gelir — yalnızca gerçekten güvenilen, sözleşmeli bir sistem için
kullanılmalıdır; tek bir sızan partner token'ı tüm müşteri verisini açar.

> Bu sınıf gerçek iş kuralının **yerini tutmaz**, yalnızca güvenli bir başlangıç noktasıdır.
> Partner-müşteri ilişkisi ileride bir veritabanı tablosuna/sözleşme yönetim sistemine
> bağlanacaksa, bu port başka bir sınıfla yeniden implemente edilmelidir — `IA2ASubjectAuthorizer`
> arayüzü sabit kaldığı sürece çağıran taraf ([`A2ATokenExchangeService`](A2ATokenExchangeService.md))
> hiç değişmez. Bu, Dependency Inversion prensibinin klasik bir uygulamasıdır: iş kuralı arkasında
> değişebilir bir implementasyon.

`A2AOptions` içindeki `MaxMessageChars`/`MaxParts`/`MaxRequestBytes` gibi limitler, LLM'e istek
gitmeden ÖNCE, Api katmanında uygulanır — maliyet zaten oluştuktan sonra sınır koymanın faydası
olmadığı için (bkz. sınıf içi yorum).

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `CanActForCustomerAsync(string partnerId, string customerId, CancellationToken ct = default): Task<bool>` | Partner tanımlı mı ve müşteri izin listesinde mi (veya `"*"` var mı) kontrol eder; girdi boşsa da `false`. |

### `A2AOptions` (appsettings.json → `A2A`)

| Alan | Varsayılan | Açıklama |
|---|---|---|
| `Enabled` | `false` | Kapalıysa A2A endpoint'leri hiç map edilmez. |
| `SubjectTokenMinutes` | `5` | Özne token ömrü. |
| `PublicBaseUrl` | `""` | Agent card'larda ilan edilecek dış adres; boşsa göreli URL kullanılır (proxy arkasında yanlış çözülebilir). |
| `RequestsPerMinute` | `60` | Partner başına dakikalık istek sınırı. |
| `MaxMessageChars` | `4000` | Tek çağrıdaki toplam metin karakter sınırı (LLM öncesi uygulanır). |
| `MaxParts` | `20` | Tek çağrıdaki azami parça sayısı (uzunluk sınırını küçük parçalara bölerek atlatmayı engeller). |
| `MaxRequestBytes` | `64 * 1024` | Azami istek gövdesi (Kestrel varsayılanı 30MB'dır, metin kanalı için gereksiz geniştir). |
| `DocumentationUrl` | `""` | Kök agent card'ında `documentationUrl`; boşsa alan hiç yazılmaz. |
| `Partners` | `[]` | `A2APartnerOptions` listesi. |

### `A2APartnerOptions`

| Alan | Açıklama |
|---|---|
| `PartnerId` | Partnerin kimliği. |
| `AllowedCustomerIds` | Bu partnerin adına hareket edebileceği müşteri kimlikleri; `"*"` = tümü. |

## 7. Bağımlılıklar (Constructor Injection)

- `IOptions<A2AOptions>` — partner listesi ve limitler.
- `ILogger<ConfiguredA2ASubjectAuthorizer>` — tanımsız partner/yetkisiz müşteri denemelerini loglar.

## Bağlantılar

- [A2ATokenExchangeService.md](A2ATokenExchangeService.md) — bu kararı tüketen taraf
- [A2ASubjectIdentity.md](A2ASubjectIdentity.md) — üretilen token'ın kimlik biçimi

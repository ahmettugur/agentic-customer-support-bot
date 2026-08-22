# ConfiguredA2ASubjectAuthorizer

- **Kaynak:** `CustomerSupportBot.Application/Services/A2A/ConfiguredA2ASubjectAuthorizer.cs`
- **Tür:** `public sealed class : IA2ASubjectAuthorizer`
- **Namespace:** `CustomerSupportBot.Application.Services.A2A`

## Ne işe yarar?

`ConfiguredA2ASubjectAuthorizer`, Application/Services/A2A/ConfiguredA2ASubjectAuthorizer.cs IA2ASubjectAuthorizer'ın yapılandırma tabanlı, VARSAYILAN OLARAK REDDEDEN implementasyonu. <summary> Partner → müşteri yetkisini <see cref="A2AOptions"/> üzerinden okur.  <para> <b>Varsayılan davranış REDDETMEKTİR.</b> Hiçbir yapılandırma yoksa, partner tanımlı değilse veya müşteri o partnerin listesinde değilse <c>false</c> döner. Bu bilinçli: A2A kanalı dış sistemlere açık ve bu kapı yanlış açıldığında bedeli başka bir müşterinin sipariş geçmişidir. "Yapılandırmayı unuttum" durumunun sonucu <b>sızıntı değil, çalışmama</b> olmalıdır. </para>  <para> Bu sınıf gerçek iş kuralının <b>yerini tutmaz</b>, yalnızca güvenli bir başlangıç noktasıdır. Partner-müşteri ilişkisi bir tabloya/sözleşmeye bağlanacaksa bu port yeniden implemente edilmeli; çağıran taraf hiç değişmez. </para> </summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ConfiguredA2ASubjectAuthorizer`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public ConfiguredA2ASubjectAuthorizer(IOptions<A2AOptions> options,
        ILogger<ConfiguredA2ASubjectAuthorizer> logger)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `CanActForCustomerAsync`
```csharp
public Task<bool> CanActForCustomerAsync(string partnerId, string customerId, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Özellikler/Properties

- `Enabled` (`bool`): İlgili veriyi temsil eden özellik.
- `SubjectTokenMinutes` (`int`): İlgili veriyi temsil eden özellik.
- `PublicBaseUrl` (`string`): İlgili veriyi temsil eden özellik.
- `RequestsPerMinute` (`int`): İlgili veriyi temsil eden özellik.
- `MaxMessageChars` (`int`): İlgili veriyi temsil eden özellik.
- `MaxParts` (`int`): İlgili veriyi temsil eden özellik.
- `MaxRequestBytes` (`long`): İlgili veriyi temsil eden özellik.
- `DocumentationUrl` (`string`): İlgili veriyi temsil eden özellik.
- `Partners` (`List<A2APartnerOptions>`): İlgili veriyi temsil eden özellik.
- `PartnerId` (`string`): İlgili veriyi temsil eden özellik.
- `AllowedCustomerIds` (`List<string>`): İlgili veriyi temsil eden özellik.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IA2ASubjectAuthorizer`

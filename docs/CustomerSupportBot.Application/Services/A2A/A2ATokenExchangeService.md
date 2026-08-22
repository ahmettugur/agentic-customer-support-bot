# A2ARoles

- **Kaynak:** `CustomerSupportBot.Application/Services/A2A/A2ATokenExchangeService.cs`
- **Tür:** `public static class`
- **Namespace:** `CustomerSupportBot.Application.Services.A2A`

## Ne işe yarar?

`A2ARoles`, Application/Services/A2A/A2ATokenExchangeService.cs Partner makine kimliği → tek müşteriye kilitli, kısa ömürlü ÖZNE token'ı. <summary>A2A kanalında kullanılan roller.</summary> <summary> Partner makine kimliği. Müşteri bağımsız ürün ajanını doğrudan çağırabilir; müşteri verisi döndüren ajanlar için önce tek müşteriye kilitli bir özne token'ı almalıdır. </summary> <summary> Değişimle üretilen özne token'ı — tek bir müşteriye kilitlidir ve yalnızca A2A endpoint'lerinde geçerlidir. Sohbet/sesli kanal <c>"Customer"</c> rolü ister, bu rol oraya girmez; tersi de geçerlidir. Ayrım bilinçli: bir kanalın token'ı diğerinde kullanılamasın. </summary> <summary>Değişim sonucu.</summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`A2ARoles`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Metotlar ve İç Çalışma Mantıkları

### `A2ATokenResult`
```csharp
public sealed record A2ATokenResult(string AccessToken, DateTime ExpiresAt, string CustomerId)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `ExchangeAsync`
```csharp
public async Task<A2ATokenResult?> ExchangeAsync(
        string partnerId, string customerId, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`

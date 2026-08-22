# ExceptionTranslator

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/ExceptionTranslator.cs`
- **Tür:** `internal static class`
- **Namespace:** `CustomerSupportBot.Adapters.Agents`

## Ne işe yarar?

`ExceptionTranslator`, Microsoft Agents Framework (MAF) veya HTTP/ağ katmanından fırlatılan teknik istisnaları (`InvalidOperationException`, `TaskCanceledException`, `HttpRequestException` vb.) Domain katmanının anlayacağı [ExternalServiceException](../CustomerSupportBot.Domain/Exceptions/ExternalServiceException.md) türüne dönüştüren yardımcı sınıftır.

## Hangi amaçla kullanılır`?

Üst katmanların altyapı bağımlılığı olan MAF framework tiplerine bağımlı olmadan, temiz ve standart domain istisnalarını yakalayabilmesini sağlamak amacıyla kullanılır.

## Sorumlulukları

- **Üstlendiği:**
  - Yakalanan istisna türlerini eşleştirmek (pattern matching) ve uygun `DomainException` üretmek.

## Metotlar / Üyeler

| Üye | Tür | İmza / Tanım | Açıklama |
|---|---|---|---|
| `Translate` | Metot | `public static DomainException Translate(Exception ex, string? context = null)` | İstisnayı `ExternalServiceException` nesnesine çevirir. |

## Bağımlılıklar

- [DomainException](../CustomerSupportBot.Domain/Exceptions/DomainException.md)
- [ExternalServiceException](../CustomerSupportBot.Domain/Exceptions/ExternalServiceException.md)

# ExternalServiceException

- **Kaynak:** `CustomerSupportBot.Domain/Exceptions/ExternalServiceException.cs`
- **Tür:** `public class : DomainException`
- **Namespace:** `CustomerSupportBot.Domain.Exceptions`

## Ne işe yarar?

`ExternalServiceException`, Dış servis (OpenAI, Qdrant, Redis, Postgres) çağrılarında meydana gelen altyapı ve ağ istisnalarını temsil eder.

## Hangi amaçla kullanılır?

- Altyapı veya iş kuralı hatalarını domain katmanında standart ve tip güvenli olarak sarmalamak.
- API katmanındaki `DomainExceptionHandler` tarafından uygun HTTP durum kodlarına dönüştürülmesini sağlamak.

## Bağımlılıklar

- [DomainException](DomainException.md)

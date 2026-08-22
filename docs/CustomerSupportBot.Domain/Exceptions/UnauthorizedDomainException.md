# UnauthorizedDomainException

- **Kaynak:** `CustomerSupportBot.Domain/Exceptions/UnauthorizedDomainException.cs`
- **Tür:** `public class : DomainException`
- **Namespace:** `CustomerSupportBot.Domain.Exceptions`

## Ne işe yarar?

`UnauthorizedDomainException`, Yetkisiz müşteri erişimi veya oturum kimlik doğrulaması uyuşmazlığında fırlatılır.

## Hangi amaçla kullanılır?

- Altyapı veya iş kuralı hatalarını domain katmanında standart ve tip güvenli olarak sarmalamak.
- API katmanındaki `DomainExceptionHandler` tarafından uygun HTTP durum kodlarına dönüştürülmesini sağlamak.

## Bağımlılıklar

- [DomainException](DomainException.md)

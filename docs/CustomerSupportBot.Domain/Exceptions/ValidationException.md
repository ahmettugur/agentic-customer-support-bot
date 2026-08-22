# ValidationException

- **Kaynak:** `CustomerSupportBot.Domain/Exceptions/ValidationException.cs`
- **Tür:** `public class : DomainException`
- **Namespace:** `CustomerSupportBot.Domain.Exceptions`

## Ne işe yarar?

`ValidationException`, Kullanıcı girdisi veya model parametresi iş kurallarını (iş kuralı doğrulaması) ihlal ettiğinde fırlatılır.

## Hangi amaçla kullanılır?

- Altyapı veya iş kuralı hatalarını domain katmanında standart ve tip güvenli olarak sarmalamak.
- API katmanındaki `DomainExceptionHandler` tarafından uygun HTTP durum kodlarına dönüştürülmesini sağlamak.

## Bağımlılıklar

- [DomainException](DomainException.md)

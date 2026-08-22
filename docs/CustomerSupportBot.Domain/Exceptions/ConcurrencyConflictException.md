# ConcurrencyConflictException

- **Kaynak:** `CustomerSupportBot.Domain/Exceptions/ConcurrencyConflictException.cs`
- **Tür:** `public class : DomainException`
- **Namespace:** `CustomerSupportBot.Domain.Exceptions`

## Ne işe yarar?

`ConcurrencyConflictException`, İyimser kilitlenme (Optimistic Concurrency) veya yarış durumlarında (aynı kaydın eşzamanlı değiştirilmesi) fırlatılır.

## Hangi amaçla kullanılır?

- Altyapı veya iş kuralı hatalarını domain katmanında standart ve tip güvenli olarak sarmalamak.
- API katmanındaki `DomainExceptionHandler` tarafından uygun HTTP durum kodlarına dönüştürülmesini sağlamak.

## Bağımlılıklar

- [DomainException](DomainException.md)

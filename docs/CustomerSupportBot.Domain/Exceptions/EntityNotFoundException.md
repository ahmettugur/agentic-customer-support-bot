# EntityNotFoundException

- **Kaynak:** `CustomerSupportBot.Domain/Exceptions/EntityNotFoundException.cs`
- **Tür:** `public class : DomainException`
- **Namespace:** `CustomerSupportBot.Domain.Exceptions`

## Ne işe yarar?

`EntityNotFoundException`, Veritabanında veya bellek ambarında aranan bir varlık (Müşteri, Sipariş, Şikayet, Makale) bulunamadığında fırlatılır.

## Hangi amaçla kullanılır?

- Altyapı veya iş kuralı hatalarını domain katmanında standart ve tip güvenli olarak sarmalamak.
- API katmanındaki `DomainExceptionHandler` tarafından uygun HTTP durum kodlarına dönüştürülmesini sağlamak.

## Bağımlılıklar

- [DomainException](DomainException.md)

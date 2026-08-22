# ICustomerAuthService

- **Kaynak:** `CustomerSupportBot.Application/Ports/Inbound/Auth/ICustomerAuthService.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Inbound.Auth`

## Ne işe yarar?

`ICustomerAuthService`, <summary> Yeni müşteri hesabı kaydı. E-posta zaten kullanımdaysa veya customerId (mevcut CustomerEntity) geçerli değilse null döner ve error açıklaması taşır. </summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ICustomerAuthService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`

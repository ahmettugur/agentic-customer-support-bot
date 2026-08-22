# IUserAuthRepository

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/Auth/IUserAuthRepository.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound.Auth`

## Ne işe yarar?

`IUserAuthRepository`, Application/Ports/Driven/Auth/IUserAuthRepository.cs Secondary port — user authentication persistence. <summary> Yeni bir kullanıcı hesabı oluşturur (müşteri self-servis kaydı için). Username zaten alınmışsa null döner — çağıran taraf (ör. CustomerAuthService) bunu "e-posta kullanımda" olarak yorumlar. </summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IUserAuthRepository`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`

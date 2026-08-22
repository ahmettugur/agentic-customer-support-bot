# AuthResponse

- **Kaynak:** `CustomerSupportBot.Application/Ports/Inbound/Auth/AuthResponse.cs`
- **Tür:** `public sealed record`
- **Namespace:** `CustomerSupportBot.Application.Ports.Inbound.Auth`

## Ne işe yarar?

`AuthResponse`, Ports/Driving/Auth/AuthResponse.cs ITokenService driving port'unun use case output DTO'su. <summary> Authentication yanıtı — use case boundary output. </summary> <param name="Username">Giriş kimliği — müşteri hesaplarında e-posta.</param> <param name="FullName"> Müşterinin katalogdaki adı soyadı (yalnızca Customer rolünde dolu; staff hesaplarında null). Arayüzün kullanıcıya adıyla hitap edebilmesi için döner — <see cref="Username"/> e-posta olduğundan tek başına gösterime uygun değil. Kullanıcının KENDİ verisi olduğu için ek bir yetkilendirme gerektirmez; başka müşterinin adı bu yolla asla dönmez (kimlik JWT'nin bağlı olduğu hesaptan çözülür). </param>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`AuthResponse`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`

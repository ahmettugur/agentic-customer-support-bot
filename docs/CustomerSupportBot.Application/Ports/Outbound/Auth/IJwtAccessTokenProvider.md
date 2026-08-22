# IJwtAccessTokenProvider

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/Auth/IJwtAccessTokenProvider.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound.Auth`

## Ne işe yarar?

`IJwtAccessTokenProvider`, <summary> JWT access token üretimi için secondary (driven) port. </summary> <summary> Erişim token'ı üretir. </summary> <param name="lifetimeMinutes"> Token ömrü. <c>null</c> ise yapılandırmadaki varsayılan (<c>AccessTokenMinutes</c>) kullanılır — mevcut tüm çağıranlar bu davranışı korur. A2A özne token'ları gibi tek bir çağrı için üretilen, dış sisteme verilen token'lar bilinçli olarak çok daha kısa bir ömürle istenir. </param>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IJwtAccessTokenProvider`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`

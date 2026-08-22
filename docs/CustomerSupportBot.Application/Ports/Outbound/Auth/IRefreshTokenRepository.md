# IRefreshTokenRepository

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/Auth/IRefreshTokenRepository.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound.Auth`

## Ne işe yarar?

`IRefreshTokenRepository`, Application/Ports/Driven/Auth/IRefreshTokenRepository.cs Secondary port — refresh token persistence. <summary> Yalnızca kayıt HÂLÂ iptal edilmemişse iptal eder — koşullu sahiplenme.  <para> <see cref="RevokeAsync"/>'in aksine oku-değiştir-yaz DEĞİLDİR: tek bir koşullu UPDATE'tir (<c>WHERE Id = id AND RevokedAt IS NULL</c>). Fark önemlidir — aynı refresh token'la eşzamanlı gelen iki yenileme isteği ikisi de "hâlâ geçerli" okuyup ikisi de yeni bir token üretebilirdi; tek bir çalıntı/paylaşılan token'dan iki geçerli oturum zinciri doğar ve yeniden kullanım tespiti sessizce atlanırdı. Bu metotla yalnızca BİR çağıran satırı gerçekten değiştirir; kaybeden <c>false</c> alır ve reddedilmelidir. </para> </summary> <returns>Bu çağrı kaydı iptal ettiyse <c>true</c>; kayıt zaten iptal edilmişse <c>false</c>.</returns>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IRefreshTokenRepository`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`

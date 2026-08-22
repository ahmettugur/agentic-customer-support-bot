# CustomerSupportBot.Adapters.Persistence.Auth

Bu klasör (kök `Auth/` — `EfCore/Auth/` ile karıştırılmamalı, o klasör EF Core repository implementasyonlarını barındırır, bu klasör ise kimlik doğrulama **algoritmalarını**) parola hash'leme ve JWT üretimi gibi kriptografik/kimlik doğrulama detaylarını izole eden adaptörleri barındırır.

## Dosyalar

- [BCryptPasswordHasher](BCryptPasswordHasher.md) — `IPasswordHasher` portu; BCrypt ile parola hash/doğrulama.
- [JwtAccessTokenProvider](JwtAccessTokenProvider.md) — `IJwtAccessTokenProvider` portu; imzalı JWT erişim token'ı üretimi.
- ⚠️ [TokenService](TokenService.md) — **artık sadece yorum içeren boş dosya**, sorumlulukları `TokenPortService` (Application) ve `JwtAccessTokenProvider`'a bölündü.

## İlgili

- [EfCore/Auth/](../EfCore/README.md) — `EfUserAuthRepository`, `EfRefreshTokenRepository` (veritabanı erişimi).
- `TokenPortService` (Application katmanı) — login/refresh orkestrasyonu, bu klasördeki iki servisi birlikte kullanır.

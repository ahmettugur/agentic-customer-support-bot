# AppAuthStateProvider

## Ne İşe Yarar
Blazor'un `AuthenticationStateProvider` soyut sınıfını uygular; `localStorage`'daki JWT token verisinden `ClaimsPrincipal` üretir.

## Hangi Amaçla Kullanılır
Blazor'un `<AuthorizeRouteView>`, `<AuthorizeView>` ve `[Authorize]` attribute mekanizmalarının çalışması için gereken kimlik bilgisini sağlar. Tüm sayfa ve bileşenlerde yetki kontrolünün temeli bu sınıftır.

## Sorumlulukları
- `localStorage`'dan JWT token okumak (`AuthTokenStore` üzerinden).
- Token mevcutsa `Username` ve `Role` claim'leri içeren `ClaimsPrincipal` oluşturmak.
- Token yoksa anonim kimlik döndürmek.
- Login/logout sonrası Blazor cascade'ini tetiklemek (`NotifyStateChanged`).

## Diğer Katman ve Bileşenlerle İlişkileri
- **DI ile inject edilen**: [AuthTokenStore](AuthTokenStore.md).
- **Blazor kayıt**: `Program.cs`'de `AuthenticationStateProvider` olarak kaydedilir.
- **Kullanan bileşenler**: Tüm `[Authorize]`'lı sayfalar, [AdminApiService](AdminApiService.md) (rol tespiti için), [AuthorizedHttpClientHandler](AuthorizedHttpClientHandler.md).

## Kullanılma Nedeni ve Tasarım Yaklaşımı
Blazor WASM'da sunucu tarafı session olmadığı için kimlik bilgisi `localStorage`'dan okunur. JWT decode etmek yerine `AuthTokenData` record'ından doğrudan claim üretilir — bu daha basit ve güvenilirdir (token'ın claim'leri sunucu tarafında zaten doğrulanmıştır).

## Metotlar / Üyeler

| Metot | Açıklama |
|-------|----------|
| `GetAuthenticationStateAsync()` | `AuthTokenStore`'dan token okur; varsa claim'li principal, yoksa anonim döner. |
| `NotifyStateChanged()` | Auth state değişikliğini Blazor cascade'ine yayar. Login/logout sonrası çağrılır. |

## Bağımlılıklar
- [AuthTokenStore](AuthTokenStore.md) — Token okuma.
- `Microsoft.AspNetCore.Components.Authorization` — `AuthenticationStateProvider` base class.

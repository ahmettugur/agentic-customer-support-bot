# AppAuthStateProvider

## Ne İşe Yarar
Blazor'un `AuthenticationStateProvider` soyut sınıfını uygular; `localStorage`'daki JWT token verisinden `ClaimsPrincipal` üretir.

## Hangi Amaçla Kullanılır
Blazor'un `<AuthorizeRouteView>`, `<AuthorizeView>` ve `[Authorize]` attribute mekanizmalarının çalışması için gereken kimlik bilgisini sağlar. Tüm sayfa ve bileşenlerde yetki kontrolünün temeli bu sınıftır.

## Sorumlulukları
- `localStorage`'dan JWT token okumak (`AuthTokenStore` üzerinden).
- **Token'ın süresinin dolup dolmadığını kontrol etmek** (`JwtUtils.TryGetExpiryUtc` ile `exp` claim'ini decode ederek) ve süresi dolmuşsa sessizce refresh denemek.
- Token mevcutsa (ve gerekirse refresh sonrası) `Username`, `Role`, `FullName` claim'leri içeren `ClaimsPrincipal` oluşturmak.
- Token yoksa veya refresh başarısızsa anonim kimlik döndürmek.
- Login/logout sonrası Blazor cascade'ini tetiklemek (`NotifyStateChanged`).

## Diğer Katman ve Bileşenlerle İlişkileri
- **DI ile inject edilen**: [AuthTokenStore](AuthTokenStore.md), `AuthService` (refresh çağrısı için).
- **Kullandığı statik yardımcı**: `JwtUtils.TryGetExpiryUtc` (Web katmanı — sunucu tarafındaki JWT üretim/doğrulama koduyla karışmasın diye ayrı, imza doğrulaması yapmaz).
- **Blazor kayıt**: `Program.cs`'de `AuthenticationStateProvider` olarak kaydedilir.
- **Kullanan bileşenler**: Tüm `[Authorize]`'lı sayfalar, [AdminApiService](AdminApiService.md) (rol tespiti için), [AuthorizedHttpClientHandler](AuthorizedHttpClientHandler.md).

## Kullanılma Nedeni ve Tasarım Yaklaşımı
Blazor WASM'da sunucu tarafı session olmadığı için kimlik bilgisi `localStorage`'dan okunur. `AuthTokenData` record'ındaki `Username`/`Role`/`FullName` alanları doğrudan claim'e çevrilir — bunlar için JWT'yi decode etmeye gerek yok (login/refresh yanıtında zaten düz JSON olarak geliyorlar).

> ⚠️ **Neden `exp` claim'i ayrıca decode ediliyor?** Eskiden bu sınıf yalnızca localStorage'da bir token *var mı* diye bakıyordu — süresi dolmuş bir token da "giriş yapılmış" sayılıyordu. Sonuç: kullanıcı `[Authorize]`'lı bir sayfayı (ör. `/`, chat) sorunsuz açabiliyor, ekranı görüyor, ama ilk API çağrısında 401 alıyordu — üstelik chat akışındaki ham `fetch`/`EventSource` çağrıları bu 401'i sessizce yutuyordu (bkz. [`Chat.md`](../Pages/Chat.md)), kullanıcı "login görünüyorum ama sistem çalışmıyor" durumunda kalıyordu. Artık `GetAuthenticationStateAsync` her çağrıldığında (sayfa açılışı, her navigasyon — `OnLocationChanged`) token'ın `exp`'ini kontrol ediyor; süresi dolmuşsa (30sn tampon payıyla) `AuthService.TryRefreshAsync` ile sessizce yenilemeyi dener. Refresh token da geçersizse `Anonymous` döner ve `AuthorizeRouteView` kullanıcıyı [`RedirectToLogin`](../Layout/RedirectToLogin.md)'e düşürür — artık geçersiz bir oturumla sayfaya asla girilemez.

## Metotlar / Üyeler

| Metot | Açıklama |
|-------|----------|
| `GetAuthenticationStateAsync()` | `AuthTokenStore`'dan token okur; `exp` süresi dolmuşsa `AuthService.TryRefreshAsync` ile yenilemeyi dener; sonuçta geçerli bir token varsa claim'li principal, yoksa anonim döner. |
| `NotifyStateChanged()` | Auth state değişikliğini Blazor cascade'ine yayar. Login/logout sonrası çağrılır. |

## Bağımlılıklar
- [AuthTokenStore](AuthTokenStore.md) — Token okuma/yazma.
- `AuthService` — `TryRefreshAsync` (scope başına en fazla bir eşzamanlı refresh — dedupe mantığı `AuthService`'te).
- `JwtUtils` — `exp` claim'i imza doğrulamadan decode eder (yalnızca UX amaçlı; gerçek doğrulama sunucuda).
- `Microsoft.AspNetCore.Components.Authorization` — `AuthenticationStateProvider` base class.

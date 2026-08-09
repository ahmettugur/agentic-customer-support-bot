# AuthService

## Ne İşe Yarar
Backend `/auth` endpoint'leriyle iletişim kurarak login, logout ve token refresh işlemlerini gerçekleştirir.

## Hangi Amaçla Kullanılır
`Login.razor` ve `CustomerLogin.razor` sayfalarında kullanıcı girişi yapılırken, `AuthorizedHttpClientHandler`'da 401 alındığında otomatik refresh tetiklenirken kullanılır. JavaScript'teki `auth.js`'in C# karşılığıdır.

## Sorumlulukları
- Admin/Agent login (`/auth/login`).
- Müşteri login (`/auth/customer/login`) ve kayıt (`/auth/customer/register`).
- Logout — sunucuya token invalidation bildirimi + `localStorage` temizleme.
- Token refresh — paralel çağrıları birleştirme (coalescing) ile tek bir refresh isteği gönderme.
- Başarılı auth yanıtlarını `AuthTokenStore`'a yazma.
- Hata durumlarında okunabilir Türkçe mesajlarla `InvalidOperationException` fırlatma.

## Diğer Katman ve Bileşenlerle İlişkileri
- **DI ile inject edilen**: `HttpClient` (ham, handler zincirsiz — refresh döngüsü koruması), [AuthTokenStore](AuthTokenStore.md).
- **Kullanan bileşenler**: `Login.razor`, `CustomerLogin.razor`, [AuthorizedHttpClientHandler](AuthorizedHttpClientHandler.md).
- **Backend karşılığı**: `CustomerSupportBot.Api` → `AuthEndpoints`.

## Kullanılma Nedeni ve Tasarım Yaklaşımı
`AuthService` bilinçli olarak **ham `HttpClient`** kullanır (handler zinciri olmadan). Eğer `AuthorizedHttpClientHandler`'dan geçseydi, 401 → refresh → 401 → refresh sonsuz döngüsü oluşurdu. `_refreshTask` coalescing pattern'i, eşzamanlı birden fazla bileşenin aynı anda refresh tetiklemesini önler.

## Metotlar / Üyeler

| Metot | Açıklama |
|-------|----------|
| `LoginAsync(username, password)` | Admin/Agent login. |
| `CustomerLoginAsync(email, password)` | Müşteri login. |
| `CustomerRegisterAsync(email, password, customerId)` | Müşteri kayıt. |
| `LogoutAsync()` | Sunucuya logout bildirimi + token silme. |
| `TryRefreshAsync()` | Mevcut refresh token ile yeni access token alma; paralel çağrıları birleştirir. |

## Bağımlılıklar
- `HttpClient` — Ham (handler zincirsiz).
- [AuthTokenStore](AuthTokenStore.md) — Token okuma/yazma.

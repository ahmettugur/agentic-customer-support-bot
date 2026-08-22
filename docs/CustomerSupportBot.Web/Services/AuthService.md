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
`AuthService` bilinçli olarak **ham `HttpClient`** kullanır (handler zinciri olmadan). Eğer `AuthorizedHttpClientHandler`'dan geçseydi, 401 → refresh → 401 → refresh sonsuz döngüsü oluşurdu.

İki ayrı kimlik alanı (staff ve customer, bkz. [AuthScope](AuthScope.md)) aynı tarayıcıda aynı anda aktif olabildiğinden, her metot bir `AuthScope` parametresi alır ve token'ı [AuthTokenStore](AuthTokenStore.md)'a o scope'un kendi anahtarıyla yazar/okur. `_refreshTasks` bir `Dictionary<AuthScope, Task?>` — coalescing pattern scope başına ayrı çalışır: staff sekmesinde eşzamanlı birden fazla bileşen refresh tetiklerse tek bir istek gider, ama customer scope'unun refresh'i bundan bağımsızdır (aynı anda ikisi de tetiklenebilir).

## Metotlar / Üyeler

| Metot | Açıklama |
|-------|----------|
| `LoginAsync(username, password)` | Admin/Agent login (`/auth/login`); token'ı `AuthScope.Staff` altında saklar. |
| `CustomerLoginAsync(email, password)` | Müşteri login (`/auth/customer/login`); token'ı `AuthScope.Customer` altında saklar. |
| `CustomerRegisterAsync(email, password, customerId)` | Müşteri kayıt (`/auth/customer/register`); başarılıysa doğrudan giriş yapılmış olur (`AuthScope.Customer`). |
| `LogoutAsync(AuthScope scope)` | İlgili scope'un token'ıyla sunucuya logout bildirimi gönderir, ardından o scope'un `localStorage` kaydını temizler. |
| `TryRefreshAsync(AuthScope scope)` | İlgili scope'un mevcut refresh token'ıyla yeni access token alır; aynı scope için eşzamanlı çağrıları tek isteğe birleştirir (`_refreshTasks`). |

## Bağımlılıklar
- `HttpClient` — Ham (handler zincirsiz).
- [AuthTokenStore](AuthTokenStore.md) — Token okuma/yazma.

# AuthTokenStore

## Ne İşe Yarar
Tarayıcının `localStorage`'ında JWT token verilerini (`AccessToken`, `RefreshToken`, `Username`, `Role`) saklar ve okur.

## Hangi Amaçla Kullanılır
Tüm auth akışının kalıcı depolama katmanıdır. Login sonrası token kaydedilir, sayfa yenilendiğinde oturum bu store'dan geri yüklenir, logout'ta silinir.

## Sorumlulukları
- `localStorage`'a token yazma (`cs.auth` anahtarı altında JSON olarak).
- `localStorage`'dan token okuma ve deserialize etme.
- Token yoksa veya okunamazsa `null` döndürme (exception yutma).
- Kısa yol `GetAccessTokenAsync()` ile sadece access token'ı döndürme.

## Diğer Katman ve Bileşenlerle İlişkileri
- **DI ile inject edilen**: `IJSRuntime` — JavaScript interop ile `localStorage` erişimi.
- **Kullanan sınıflar**: [AuthService](AuthService.md), [AppAuthStateProvider](AppAuthStateProvider.md), [AuthorizedHttpClientHandler](AuthorizedHttpClientHandler.md).

## Kullanılma Nedeni ve Tasarım Yaklaşımı
Blazor WASM'da `localStorage`'a erişim yalnızca JS interop üzerinden yapılabilir. `cs.auth` storage key'i JavaScript tarafındaki auth sistemiyle uyumludur (WASM'a geçiş öncesi JS tabanlı auth ile aynı key kullanılır).

## Metotlar / Üyeler

| Metot / Üye | Açıklama |
|-------------|----------|
| `ReadAsync()` | `localStorage`'dan `AuthTokenData` okur; hata veya boş ise `null`. |
| `WriteAsync(data)` | Token'ı yazar; `null` ise `localStorage`'dan siler. |
| `GetAccessTokenAsync()` | Kısa yol — yalnızca `AccessToken` string'ini döner. |
| `StorageKey` | `"cs.auth"` — localStorage anahtarı (const). |

### AuthTokenData (record)

| Property | Tip | Açıklama |
|----------|-----|----------|
| `AccessToken` | `string` | JWT access token. |
| `RefreshToken` | `string` | Refresh token. |
| `Username` | `string` | Kullanıcı adı. |
| `Role` | `string` | Kullanıcı rolü (Admin, Agent, Customer). |

## Bağımlılıklar
- `IJSRuntime` — `localStorage.getItem` / `setItem` / `removeItem` JS interop.
- `System.Text.Json` — Serialization (camelCase policy).

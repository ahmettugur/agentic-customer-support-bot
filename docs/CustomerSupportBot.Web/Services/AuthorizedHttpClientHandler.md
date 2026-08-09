# AuthorizedHttpClientHandler

## Ne İşe Yarar
`DelegatingHandler` olarak HTTP pipeline'ına eklenir; tüm API isteklerine otomatik Bearer token ekler ve 401 yanıtlarında token refresh + retry gerçekleştirir.

## Hangi Amaçla Kullanılır
`Program.cs`'de `HttpClient` oluşturulurken `InnerHandler` olarak zincire bağlanır. JavaScript'teki `authFetch` fonksiyonunun C# karşılığıdır.

## Sorumlulukları
- Giden isteklere `Authorization: Bearer <token>` header'ı eklemek.
- `/auth/*` endpoint'lerini bypass etmek (refresh döngüsü koruması).
- 401 alındığında `AuthService.TryRefreshAsync()` ile yeni token almak.
- Refresh başarılıysa isteği yeni token ile tekrar göndermek.
- Refresh başarısızsa kullanıcıyı role'üne uygun login sayfasına yönlendirmek.

## Diğer Katman ve Bileşenlerle İlişkileri
- **DI ile inject edilen**: [AuthTokenStore](AuthTokenStore.md), [AuthService](AuthService.md), `NavigationManager`, [AppAuthStateProvider](AppAuthStateProvider.md).
- **Kayıt**: `Program.cs`'de `HttpClient`'ın handler zincirinde.
- **Kullanan tüm servisler**: `AdminApiService`, `ChatApiService`, `TracesApiService` vb. (bu handler üzerinden geçen `HttpClient`'ı kullanırlar).

## Kullanılma Nedeni ve Tasarım Yaklaşımı
`DelegatingHandler` pattern'i cross-cutting concern'leri (auth, retry) HTTP pipeline'ına şeffaf şekilde eklemenin standart .NET yoludur. `CloneRequestAsync` metodu gereklidir çünkü `HttpRequestMessage` bir kez gönderildikten sonra tekrar gönderilemez — içerik stream'i tüketilmiş olur.

## Metotlar / Üyeler

| Metot | Açıklama |
|-------|----------|
| `SendAsync(request, ct)` | Override — token ekler, 401'de refresh dener, başarısızsa login'e yönlendirir. |
| `CloneRequestAsync(original)` | İsteğin header ve content kopyasını oluşturur (retry için). |

## Bağımlılıklar
- [AuthTokenStore](AuthTokenStore.md) — Token okuma.
- [AuthService](AuthService.md) — Token refresh.
- [AppAuthStateProvider](AppAuthStateProvider.md) — Auth state güncelleme.
- `NavigationManager` — Login sayfasına yönlendirme.

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
- 403 alındığında bir toast ile bildirmek — token geçerli ama rol yetmiyor demektir; aksi
  halde bu sessizce "veri yok" gibi görünürdü (bkz. aşağıdaki not).

## Diğer Katman ve Bileşenlerle İlişkileri
- **DI ile inject edilen**: [AuthTokenStore](AuthTokenStore.md), [AuthService](AuthService.md), `NavigationManager`, [AppAuthStateProvider](AppAuthStateProvider.md), [ToastService](ToastService.md).
- **Kayıt**: `Program.cs`'de `HttpClient`'ın handler zincirinde.
- **Kullanan tüm servisler**: `AdminApiService`, `ChatApiService`, `TracesApiService` vb. (bu handler üzerinden geçen `HttpClient`'ı kullanırlar).

## Kullanılma Nedeni ve Tasarım Yaklaşımı
`DelegatingHandler` pattern'i cross-cutting concern'leri (auth, retry) HTTP pipeline'ına şeffaf şekilde eklemenin standart .NET yoludur. `CloneRequestAsync` metodu gereklidir çünkü `HttpRequestMessage` bir kez gönderildikten sonra tekrar gönderilemez — içerik stream'i tüketilmiş olur.

> ⚠️ Bu handler yalnızca Blazor'un `HttpClient`'ı üzerinden giden istekleri kapsar. [`Chat.razor`](../Pages/Chat.md)'daki chat-stream/EventSource çağrıları `chat-bridge.js` içinde ham `fetch`/`EventSource` kullanır — bu pipeline'ın **dışındadır** ve aynı 401→refresh→retry mantığının ayrı bir kopyası oradadır (bkz. [`Chat.md`](../Pages/Chat.md#token-süresi-dolması--chat-akışı-authorizedhttpclienthandlerin-dışında)).

> 🐞 **Bulundu ve düzeltildi:** Admin sayfaları (`Admin`, `Traces`, `Replay`, `Sla`, `Knowledge`)
> eskiden rolsüz `[Authorize]` kullanıyordu; API tarafı ise bu uçları `Admin` rolüyle
> koruyor (`Program.cs`'deki `adminScope`). Bir `Agent` rolüyle giriş yapan kullanıcı için
> sayfa açılıyor ama her çağrı 403 dönüyordu — servisler (`TracesApiService`,
> `SlaApiService` vb.) hatayı yutup boş liste döndüğü için bu "veri yok" gibi görünüyordu.
> İki parçalı düzeltme: (1) beş sayfaya `[Authorize(Roles = "Admin")]` eklendi — API ile
> sayfa yetkisi artık örtüşüyor; (2) burada 403'ü ayırt edilebilir kılmak için toast
> eklendi, çünkü ileride benzer bir rol uyuşmazlığı yine oluşabilir.

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

# Program.cs

## Ne İşe Yarar
Blazor WebAssembly uygulamasının giriş noktasıdır. `WebAssemblyHostBuilder` ile WASM host'u yapılandırır, tüm DI servis kayıtlarını gerçekleştirir ve uygulamayı başlatır.

## Hangi Amaçla Kullanılır
Uygulamanın bootstrap aşamasında bir kez çalışır. Auth altyapısı, HTTP pipeline'ı, UI servisleri ve API servislerinin DI container'a kaydedilmesinden sorumludur.

## Sorumlulukları
- `App` ve `HeadOutlet` root bileşenlerini kaydetmek.
- Development ortamında log seviyesini `Debug`'a çekmek.
- `UnobservedTaskException` handler'ı ile yakalanmamış async hataları loglamak.
- Auth servisleri zincirini kaydetmek: `AuthTokenStore` → `AppAuthStateProvider` → `AuthenticationStateProvider`.
- `AuthorizedHttpClientHandler` ile zenginleştirilmiş `HttpClient` kaydı (Bearer token ekleme, 401 refresh).
- `AuthService` için **ayrı bir ham `HttpClient`** kaydı (refresh döngüsünü önlemek amacıyla handler zincirine bağlanmaz).
- UI servisleri: `ToastService`, `ThemeService`.
- API servisleri: `AdminApiService`, `AnalyticsApiService`, `ChatApiService`, `TracesApiService`, `SlaApiService`, `KnowledgeApiService`.

## Diğer Katman ve Bileşenlerle İlişkileri
- **Kaydedilen servisler**: Tüm `Services/` altındaki sınıflar burada DI'a eklenir.
- **Root bileşen**: [App.razor](../../CustomerSupportBot.Web/App.razor) — `Router` ve `CascadingAuthenticationState` içerir.
- **Backend adresi**: `https://localhost:7095` (development) hard-coded olarak `HttpClient.BaseAddress`'e atanır.

## Kullanılma Nedeni ve Tasarım Yaklaşımı
Blazor WASM'da `Startup` sınıfı yoktur; tüm yapılandırma `Program.cs` üzerinden yapılır. İki ayrı `HttpClient` kaydı tasarım gereğidir: `AuthorizedHttpClientHandler` 401 aldığında `AuthService.TryRefreshAsync()` çağırır — eğer `AuthService` de aynı handler'dan geçseydi sonsuz döngü oluşurdu.

## Metotlar / Üyeler
Bu dosya bir sınıf değil, top-level statements ile yazılmış giriş noktasıdır. Kayda değer kararlar:

| Kayıt | Açıklama |
|-------|----------|
| `AddAuthorizationCore()` | Blazor WASM authorization altyapısı. |
| `AddScoped<AuthTokenStore>()` | `localStorage` tabanlı JWT depolama. |
| `AddScoped<AppAuthStateProvider>()` | JWT → `ClaimsPrincipal` dönüştürücü. |
| `AddScoped<AuthorizedHttpClientHandler>()` | Bearer token + 401 retry handler. |
| `AddScoped<HttpClient>(...)` | Handler zincirli, base address'li HttpClient. |
| `AddScoped<AuthService>(...)` | Ham HttpClient'li auth servisi (döngü koruması). |

## Bağımlılıklar
- `Microsoft.AspNetCore.Components.WebAssembly.Hosting`
- `Microsoft.AspNetCore.Components.Authorization`
- Proje içi: `CustomerSupportBot.Web.Services` namespace'indeki tüm servisler.

# AdminNavBar

**Dosya:** `Layout/AdminNavBar.razor`

## Ne İşe Yarar

Admin/Agent panelinin üst navigasyon barıdır: sayfa linkleri, tema (dark/light) toggle,
aktif kullanıcı adı ve çıkış butonunu tek bir yatay bar'da sunar.

## Hangi Amaçla Kullanılır

[`AdminLayout`](AdminLayout.md) tarafından her admin/agent sayfasının üstünde sabit olarak
gösterilir. Kullanıcının panel içi sayfalar arasında gezinmesini ve oturumunu yönetmesini sağlar.

## Sorumlulukları

- Navigasyon linkleri: Sohbet (`/`), HITL Panel (`/admin`), Trace Dashboard (`/traces`),
  Replay (`/replay`), SLA (`/sla`), Bilgi Tabanı (`/knowledge`) — `NavLink` ile aktif sayfa
  otomatik vurgulanır.
- Dark/light tema toggle butonu; ilk render'da [`ThemeService.EnsureInitAsync`](../Services/ThemeService.md)
  ile mevcut tema tercihini yükler ve `ThemeSvc.OnChange` olayına abone olup değişince
  `StateHasChanged()` çağırır.
- `AuthorizeView` içinde, giriş yapmış kullanıcının adını (`context.User.Identity?.Name`)
  göstermek.
- Çıkış butonu: [`AuthService.LogoutAsync(AuthScope.Staff)`](../Services/AuthService.md) çağırıp
  [`AppAuthStateProvider.NotifyStateChanged()`](../Services/AppAuthStateProvider.md) ile Blazor
  auth cascade'ini tetiklemek, ardından `/login`'e yönlendirmek.

## Diğer Katman ve Bileşenlerle İlişkileri

- **`@inject` ile alınanlar**: `NavigationManager`, [`AuthService`](../Services/AuthService.md),
  [`AppAuthStateProvider`](../Services/AppAuthStateProvider.md), [`ThemeService`](../Services/ThemeService.md).
- `IDisposable` uygular — `Dispose()` içinde `ThemeSvc.OnChange -= StateHasChanged` ile aboneliği
  kaldırır (memory leak önleme).
- CSS: `AdminNavBar.razor.css` (component-scoped).
- Barındıran bileşen: [`AdminLayout`](AdminLayout.md).

## Kullanılma Nedeni ve Tasarım Yaklaşımı

Logout işlemi [`AuthScope.Staff`](../Services/AuthScope.md) ile çağrılır — bu, uygulamanın
Admin/Agent ve Customer kimliklerini ayrı token çiftleri olarak tuttuğu çoklu-kimlik-alanı
tasarımının bir parçasıdır (bkz. [`AuthService`](../Services/AuthService.md)): burada çıkış
yapmak müşteri tarafındaki (varsa) ayrı oturumu etkilemez.

Tema durumu component-local değil `ThemeService` üzerinden global tutulur, böylece
[`MainLayout`](MainLayout.md)/[`NavMenu`](NavMenu.md) tarafındaki tema toggle ile senkron kalır.

## Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `OnAfterRenderAsync(bool firstRender)` | İlk render'da tema servisini başlatır ve `OnChange` olayına abone olur. |
| `ToggleAsync()` | `ThemeSvc.ToggleAsync()` çağırarak dark/light temayı değiştirir. |
| `LogoutAsync()` | Staff oturumunu kapatır, auth cascade'i tetikler, `/login`'e yönlendirir. |
| `Dispose()` | `ThemeSvc.OnChange` aboneliğini kaldırır. |

## Bağımlılıklar

- [`AuthService`](../Services/AuthService.md) — Logout.
- [`AppAuthStateProvider`](../Services/AppAuthStateProvider.md) — Auth state değişikliği bildirimi.
- [`ThemeService`](../Services/ThemeService.md) — Tema okuma/değiştirme.
- `NavigationManager` — Logout sonrası yönlendirme.

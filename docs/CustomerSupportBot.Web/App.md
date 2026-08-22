# App

**Dosya:** `App.razor`

## Ne İşe Yarar
Blazor WebAssembly uygulamasının kök bileşenidir. `wwwroot/index.html` içine tek sefer render
edilir ve içine router'ı, kimlik doğrulama cascade'ini ve global hata sınırını (`ErrorBoundary`)
kurar.

## Hangi Amaçla Kullanılır
Uygulama açıldığında ilk çalışan bileşendir. Tüm sayfa navigasyonu (`Router`), rol bazlı erişim
kontrolü (`AuthorizeRouteView`) ve beklenmeyen istisnaların yakalanması burada merkezi olarak
kurulur — her sayfa bunu tek tek yapmaz.

## Sorumlulukları
- `Router` ile route eşleştirmesi yapmak; `App.Assembly`'i tarayarak `[page]` direktifli tüm
  bileşenleri route tablosuna eklemek.
- Bulunamayan route'ları `NotFound.razor`'a yönlendirmek (`NotFoundPage`).
- `CascadingAuthenticationState` ile [AppAuthStateProvider](Services/AppAuthStateProvider.md)'ın
  ürettiği kimlik bilgisini tüm alt bileşenlere cascade parametre olarak yaymak.
- Yetkisiz erişimde (`NotAuthorized`) [RedirectToLogin](Layout/RedirectToLogin.md) bileşenini
  göstermek; yetkilendirme sürerken (`Authorizing`) bir bekleme mesajı göstermek.
- `ErrorBoundary` ile render sırasında fırlayan beklenmeyen istisnaları yakalamak — sayfa
  tamamen beyaz ekrana düşmek yerine hata mesajı + "Sayfayı Yenile"/"Hatayı Kapat" seçenekleri
  gösterir.

## Diğer Katman ve Bileşenlerle İlişkileri
- **Kullandığı bileşenler**: `Router`, `CascadingAuthenticationState`, `AuthorizeRouteView`,
  `ErrorBoundary`, [RedirectToLogin](Layout/RedirectToLogin.md), `FocusOnNavigate`.
- **Varsayılan layout**: `DefaultLayout="@typeof(MainLayout)"` — route'a özel `@layout`
  direktifi yoksa (ör. [Login](Pages/Login.md), [CustomerLogin](Pages/CustomerLogin.md) gibi
  sayfalar kendi `@layout EmptyLayout`'unu seçer) [MainLayout](Layout/MainLayout.md) kullanılır.
- `Program.cs`'te `builder.RootComponents.Add<App>("#app")` ile `index.html`'e bağlanır.

## Kullanılma Nedeni ve Tasarım Yaklaşımı
Blazor WASM'ın standart "tek kök bileşen" deseni. `ErrorBoundary`'nin burada, en üst seviyede
kurulması bilinçlidir: alt bileşenlerden herhangi biri (örn. bir API çağrısı sonrası JSON
deserialize hatası) render sırasında istisna fırlatırsa, tüm SPA'nın çökmesi yerine kullanıcı
anlaşılır bir hata ekranı görür ve sayfayı yeniden yükleyebilir.

## Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `_errorBoundary` (alan) | `ErrorBoundary` bileşenine referans; `ResetError()` içinde kullanılır. |
| `ResetError()` | `_errorBoundary.Recover()` çağırarak hata durumunu temizler ve normal render'a döner ("Hatayı Kapat" butonu). |

## Bağımlılıklar
Yok — kurucu enjeksiyonu almaz, saf bir kök markup bileşenidir.

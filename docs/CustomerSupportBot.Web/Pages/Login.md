# Login.razor

## Ne İşe Yarar
Admin ve Agent kullanıcıları için giriş sayfasıdır.

## Hangi Amaçla Kullanılır
`/login` route'unda, yönetici paneline erişmek isteyen kullanıcıların kimlik doğrulaması için kullanılır.

## Sorumlulukları
- Kullanıcı adı ve parola formu sunmak.
- `AuthService.LoginAsync` ile backend'e login isteği göndermek.
- Başarılı login'de `AppAuthStateProvider.NotifyStateChanged()` ile Blazor auth cascade'ini tetiklemek.
- `?return=` query parametresinden dönüş URL'ini okumak.
- Zaten giriş yapılmışsa otomatik yönlendirme.
- Bölünmüş ekran: solda tanıtım alanı, sağda form; parolayı göster/gizle, müşteri girişine bağlantı. Stiller
  `login.css` içinde `.staff-login` altında — [CustomerLogin](CustomerLogin.md) aynı dosyanın eski sınıflarını
  kullanmaya devam eder, görünümü değişmez.
- Hata mesajlarını göstermek.
- Parola göster/gizle toggle.

## Diğer Katman ve Bileşenlerle İlişkileri
- **DI ile inject edilen**: [AuthService](../Services/AuthService.md), [AppAuthStateProvider](../Services/AppAuthStateProvider.md), `NavigationManager`, `AuthenticationStateProvider`.
- **Layout**: `EmptyLayout` (login sayfası tam ekran).
- **Yönlendiren bileşen**: [RedirectToLogin](../Layout/RedirectToLogin.md) — admin yollarından yönlendirme.

## Kullanılma Nedeni ve Tasarım Yaklaşımı
Development kolaylığı için default credential'lar form'da önceden doldurulmuştur (`admin` / `Admin123!`). `EmptyLayout` kullanılır çünkü login sayfasında navigasyon menüsü gereksizdir.

## Bağımlılıklar
- [AuthService](../Services/AuthService.md), [AppAuthStateProvider](../Services/AppAuthStateProvider.md).
- `login.css` — Özel login sayfası stilleri.

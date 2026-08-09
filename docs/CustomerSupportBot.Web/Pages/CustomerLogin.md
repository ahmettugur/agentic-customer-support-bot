# CustomerLogin.razor

## Ne İşe Yarar
Müşteri kullanıcıları için giriş ve kayıt sayfasıdır. Login ve Register modları arasında geçiş yapılabilir.

## Hangi Amaçla Kullanılır
`/customer-login` route'unda, müşterilerin chat arayüzüne erişmek için kimlik doğrulaması yapmasını sağlar.

## Sorumlulukları
- E-posta ve parola ile giriş formu sunmak.
- Kayıt modunda ek "Müşteri Kimlik Numarası" alanı göstermek.
- Login/Register mod geçişi.
- `AuthService.CustomerLoginAsync` / `CustomerRegisterAsync` ile backend iletişimi.
- Başarılı auth'da Blazor cascade tetikleme ve dönüş URL'ine yönlendirme.

## Diğer Katman ve Bileşenlerle İlişkileri
- **DI ile inject edilen**: [AuthService](../Services/AuthService.md), [AppAuthStateProvider](../Services/AppAuthStateProvider.md), `NavigationManager`, `AuthenticationStateProvider`.
- **Layout**: `EmptyLayout`.
- **Yönlendiren bileşen**: [RedirectToLogin](../Layout/RedirectToLogin.md) — müşteri yollarından yönlendirme.
- **Backend**: `AuthEndpoints` → `/auth/customer/login`, `/auth/customer/register`.

## Kullanılma Nedeni ve Tasarım Yaklaşımı
Login ve Register tek sayfada birleştirilmiştir — `Mode` enum ile geçiş yapılır. Bu, müşteri deneyimini basitleştirir (iki ayrı sayfa yerine tek sayfa).

## Bağımlılıklar
- [AuthService](../Services/AuthService.md), [AppAuthStateProvider](../Services/AppAuthStateProvider.md).
- `login.css` — Login/Register ortak stilleri.

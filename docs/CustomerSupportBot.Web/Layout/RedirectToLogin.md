# RedirectToLogin

## Ne İşe Yarar
Kimlik doğrulaması yapılmamış kullanıcıları uygun login sayfasına yönlendiren bileşendir.

## Hangi Amaçla Kullanılır
`App.razor` içindeki `<NotAuthorized>` bölümünde kullanılır. Yetki gerektiren bir sayfaya kimliksiz erişim yapılırsa bu bileşen devreye girer.

## Sorumlulukları
- Mevcut URL'den dönüş yolunu (`returnTo`) oluşturmak.
- Admin yollarını (`/admin/*`) → `/login`'e yönlendirmek.
- Müşteri yollarını (kök `/` dahil) → `/customer-login`'e yönlendirmek.
- Query string'de `?return=` parametresi eklemek (login sonrası geri dönüş için).

## Diğer Katman ve Bileşenlerle İlişkileri
- **DI ile inject edilen**: `NavigationManager`.
- **Hedef sayfalar**: [Login](../Pages/Login.md), [CustomerLogin](../Pages/CustomerLogin.md).
- **Çağıran bileşen**: `App.razor` → `<NotAuthorized>`.

## Kullanılma Nedeni ve Tasarım Yaklaşımı
İki farklı kullanıcı tipi (Admin/Agent vs Customer) iki farklı login sayfası gerektirir. URL prefix'ine bakarak (`admin` ile başlıyorsa staff, aksi halde müşteri) doğru login sayfasına yönlendirme yapılır.

## Bağımlılıklar
- `NavigationManager`

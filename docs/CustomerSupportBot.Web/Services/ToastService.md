# ToastService

## Ne İşe Yarar
Uygulama genelinde toast bildirimlerini tetikleyen event-based servis. Bildirim render'ından sorumlu değildir — yalnızca event yayınlar.

## Hangi Amaçla Kullanılır
Herhangi bir sayfa veya servisten kullanıcıya başarı, hata, uyarı veya bilgi mesajı göstermek istendiğinde çağrılır.

## Sorumlulukları
- `OnShow` event'i üzerinden `ToastMessage` yayınlamak.
- Dört tip kısayol metot sunmak: Success, Error, Warning, Info.

## Diğer Katman ve Bileşenlerle İlişkileri
- **Dinleyen bileşen**: [ToastContainer](../Components/ToastContainer.md) — `OnShow` event'ini dinler ve toast'ları render eder.
- **Kullanan sayfalar**: `Admin.razor`, `Knowledge.razor`, `Chat.razor` vb.

## Kullanılma Nedeni ve Tasarım Yaklaşımı
Observer pattern — publisher/subscriber ayrımı. Servis yalnızca event yayınlar, render sorumluluğu `ToastContainer` bileşenindedir. Bu sayede toast gösterimi merkezi ve tutarlıdır.

## Metotlar / Üyeler

| Metot / Üye | Açıklama |
|-------------|----------|
| `OnShow` | `Action<ToastMessage>` event — toast yayınlanınca tetiklenir. |
| `ShowSuccess(text)` | Başarı toast'ı yayınlar. |
| `ShowError(text)` | Hata toast'ı yayınlar. |
| `ShowWarning(text)` | Uyarı toast'ı yayınlar. |
| `ShowInfo(text)` | Bilgi toast'ı yayınlar. |

### İlişkili Tipler (aynı dosyada)

| Tip | Açıklama |
|-----|----------|
| `ToastType` (enum) | `Success`, `Error`, `Warning`, `Info` |
| `ToastMessage` (record) | `Text`, `Type`, otomatik `Id` (Guid). |

## Bağımlılıklar
Yok — saf C# event publisher.

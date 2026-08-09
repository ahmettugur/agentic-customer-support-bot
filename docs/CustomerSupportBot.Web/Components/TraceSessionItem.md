# TraceSessionItem

## Ne İşe Yarar
Trace oturumları listesinde tek bir oturumu temsil eden satır bileşenidir.

## Hangi Amaçla Kullanılır
`Traces.razor` sayfasındaki sol panelde her trace oturumunu göstermek için kullanılır.

## Sorumlulukları
- Oturum başlığı, trace sayısı, mesaj sayısı ve son aktivite zamanını göstermek.
- Oturum ID'sinin ilk 8 karakterini göstermek.
- Aktif seçili durumu görsel olarak belirtmek.
- Click ve Enter/Space keyboard event'lerini handle etmek (erişilebilirlik).
- Göreceli zaman formatlaması (örn. "5dk önce", "2sa önce").

## Diğer Katman ve Bileşenlerle İlişkileri
- **Parameters**: `Session` (`TraceSession`), `IsActive` (bool), `OnSessionSelected` (EventCallback).
- **Kullanan sayfa**: [Traces](../Pages/Traces.md).
- **Model bağımlılığı**: `TracesApiService` → `TraceSession` record.

## Kullanılma Nedeni ve Tasarım Yaklaşımı
`role="button"` ve `tabindex="0"` ile keyboard erişilebilirlik sağlanır. Zaman formatlaması client-side yapılır (backend'e bağımlı değil).

## Bağımlılıklar
- `TraceSession` record (TracesApiService.cs'de tanımlı).

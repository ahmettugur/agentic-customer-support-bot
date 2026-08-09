# Chat.razor

## Ne İşe Yarar
Müşteri destek chat arayüzüdür. Kullanıcının AI asistanla gerçek zamanlı metin konuşması yaptığı ana sayfadır.

## Hangi Amaçla Kullanılır
`/` route'unda, `Customer` rolüyle erişilen varsayılan sayfadır. SSE (Server-Sent Events) üzerinden streaming yanıtlar alır, mesaj gönderir, rating verir ve approval bildirimlerini gösterir.

## Sorumlulukları
- Kullanıcıdan mesaj alıp `/chat` endpoint'ine SSE ile göndermek.
- Streaming yanıtları gerçek zamanlı render etmek (karakter karakter).
- Mesaj geçmişini `localStorage` ve API'den senkronize etmek.
- Markdown rendering (Markdig kütüphanesi ile).
- Human-mode durumunu göstermek (insan temsilci bağlandığında).
- Oturum rating'i (yıldız + feedback) göndermek/göstermek.
- Approval bildirimleri (çan ikonu, panel, görüldü işaretleme, geçmiş).
- Dark/light tema toggle.

## Diğer Katman ve Bileşenlerle İlişkileri
- **DI ile inject edilen**: `IJSRuntime`, [ChatApiService](../Services/ChatApiService.md), `HttpClient`, [AuthTokenStore](../Services/AuthTokenStore.md), [ThemeService](../Services/ThemeService.md).
- **Layout**: `EmptyLayout` (tam ekran chat).
- **Authorization**: `[Authorize(Roles = "Customer")]`.
- **Backend karşılığı**: `CustomerSupportBot.Api` → `ChatEndpoints` (SSE stream).

## Kullanılma Nedeni ve Tasarım Yaklaşımı
SSE tercih edilmesinin nedeni tek yönlü streaming için WebSocket'ten daha basit olmasıdır. Mesajlar önce `localStorage`'da tutulur (offline erişim), sonra API ile senkronize edilir. `IAsyncDisposable` uygulanır çünkü SSE bağlantısının temizlenmesi gerekir.

## Bağımlılıklar
- [ChatApiService](../Services/ChatApiService.md) — Rating, approval, session API'leri.
- [AuthTokenStore](../Services/AuthTokenStore.md) — Token okuma (SSE header'ı için).
- [ThemeService](../Services/ThemeService.md) — Tema toggle.
- `Markdig` — Markdown → HTML dönüşümü.
- `IJSRuntime` — Scroll, localStorage, ses efekti vb. JS interop.

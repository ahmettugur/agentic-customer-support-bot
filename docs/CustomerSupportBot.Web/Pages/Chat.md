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
- **DI ile inject edilen**: `IJSRuntime`, [ChatApiService](../Services/ChatApiService.md), `HttpClient`, [AuthTokenStore](../Services/AuthTokenStore.md), `AuthService`, [AppAuthStateProvider](../Services/AppAuthStateProvider.md), `NavigationManager`, [ThemeService](../Services/ThemeService.md).
- **Layout**: `EmptyLayout` (tam ekran chat).
- **Authorization**: `[Authorize(Roles = "Customer")]`.
- **Backend karşılığı**: `CustomerSupportBot.Api` → `ChatEndpoints` (SSE stream).

### Token süresi dolması — chat akışı `AuthorizedHttpClientHandler`'ın DIŞINDA

`chat-bridge.js`'teki `__streamChat` (mesaj gönderme, `POST /chat/stream`) ve `_startPersistentEvents` (bildirim kanalı, `EventSource`/`GET /chat/events/{sid}`) ham `fetch`/`EventSource` kullanır — bunlar Blazor `HttpClient` pipeline'ının **dışındadır**, yani [`AuthorizedHttpClientHandler`](../Services/AuthorizedHttpClientHandler.md)'ın 401→refresh→retry mantığı burada devreye girmez. `window._authToken`, sayfa açılışında (`OnAfterRenderAsync` → `__chatSetup`) **bir kez** JS tarafına kopyalanır ve bir daha otomatik güncellenmez.

Bu boşluk gerçek bir kullanıcı sorununa yol açtı: access token sayfa açıkken süresi dolduğunda, `__streamChat`'in `!r.ok` dalı yalnızca `OnStreamError('HTTP 401')` çağırıyor, `_startPersistentEvents`'in `es.onerror`'ı ise **tamamen boştu** (`function () {}`) — kullanıcı "login görünüyorum ama sistem çalışmıyor" durumuna sessizce düşüyordu (bkz. [`AppAuthStateProvider.md`](../Services/AppAuthStateProvider.md) — sayfa açılışındaki proaktif kontrol de aynı sebeple eksikti).

Düzeltme, `AuthorizedHttpClientHandler`'daki mantığın buraya da taşınmasıdır:

| Kanal | JS tarafı | C# tarafı (JSInvokable) |
|---|---|---|
| `__streamChat` (mesaj gönderme) | `r.status===401 && !isRetry` → `OnStreamUnauthorized` çağırır, fetch burada biter | `AuthService.TryRefreshAsync` dener → başarılıysa `__setAuthToken` ile token'ı günceller ve `__streamChat`'i `isRetry=true` ile bir kez tekrar çağırır; başarısızsa `/customer-login`'e yönlendirir |
| `_startPersistentEvents` (`EventSource`) | `es.onerror` bağlantı başına **en fazla bir kez** (`_persistentEventsRetried` bayrağı) `OnPersistentEventsError` çağırır | Aynı refresh mantığı; başarılıysa `_startPersistentEvents(sid, true)` ile yeniden bağlanır (`isAuthRetry=true` → bayrak sıfırlanmaz, ikinci hata tekrar C#'a bildirilmez — sonsuz döngü engellenir) |

`isRetry`/`isAuthRetry` bayrakları olmadan bu mekanizma sonsuz bir JS↔C# döngüsüne girebilirdi: refresh "başarılı" ama sunucu yeni token'ı da reddederse (ör. hesap devre dışı bırakıldıysa), her retry yeni bir 401 üretir ve her 401 yeni bir refresh dener.

## Kullanılma Nedeni ve Tasarım Yaklaşımı
SSE tercih edilmesinin nedeni tek yönlü streaming için WebSocket'ten daha basit olmasıdır. Mesajlar önce `localStorage`'da tutulur (offline erişim), sonra API ile senkronize edilir. `IAsyncDisposable` uygulanır çünkü SSE bağlantısının temizlenmesi gerekir.

## Bağımlılıklar
- [ChatApiService](../Services/ChatApiService.md) — Rating, approval, session API'leri.
- [AuthTokenStore](../Services/AuthTokenStore.md) — Token okuma (SSE header'ı için).
- [ThemeService](../Services/ThemeService.md) — Tema toggle.
- `Markdig` — Markdown → HTML dönüşümü.
- `IJSRuntime` — Scroll, localStorage, ses efekti vb. JS interop.

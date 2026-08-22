# Gerçek Zamanlı İletişim Araçları (SSE ve WebSocket)

- **Kaynaklar:**
  - `CustomerSupportBot.Api/Infrastructure/SseWriter.cs`
  - `CustomerSupportBot.Api/Infrastructure/SseForwarder.cs`
  - `CustomerSupportBot.Api/Infrastructure/WebSocketBrowserChannel.cs`
- **Namespace:** `CustomerSupportBot.Api.Infrastructure`

## 1. Ne İşe Yarar

Üç küçük yardımcı sınıf, chat/realtime endpoint'lerinin kullandığı iki gerçek zamanlı iletişim
biçimini (Server-Sent Events ve WebSocket) düşük seviyeli protokol detaylarından soyutlar.

## 2. Hangi Amaçla Kullanılır

- **`SseWriter`** (internal, statik) — tek bir SSE olayını doğru çerçeveleme (`event: ...\ndata: ...\n\n`)
  ile response gövdesine yazan çekirdek yardımcı. Proxy/nginx buffering'ini kapatan header'ları
  da (`X-Accel-Buffering: no`) yazar.
- **`SseForwarder`** (public, `IDisposable`) — bir SSE bağlantısı boyunca **eşzamanlı** yazma
  isteklerini (ör. hem ana akış hem HITL abonelik geri çağrısı aynı response'a yazmak
  isteyebilir) `SemaphoreSlim` ile sıraya koyan sarmalayıcı; `ChatEndpoints`/`AdminEndpoints`'te
  kullanılır.
- **`WebSocketBrowserChannel`** — `IBrowserChannel` port'unun somut WebSocket implementasyonu;
  `RealtimeEndpoints` bunu oluşturur, Application katmanındaki `IRealtimeBridge`/
  `IRealtimeNativeBridge` yalnızca soyut `IBrowserChannel`'a bağımlıdır (hexagonal ayrım).

## 3. Sorumlulukları

- `SseWriter`: JSON serileştirme (camelCase, gevşek escape) + SSE çerçeveleme + flush.
- `SseForwarder`: `SseWriter`'ı thread-safe hale getirmek, iptal/bağlantı-kopması durumlarını
  sessizce yutmak (SSE'de istemci kapanması normal bir durumdur, exception fırlatmak akışı
  gereksiz kırar).
- `WebSocketBrowserChannel`: WebSocket alım döngüsünü (`ReceiveAsync` + `EndOfMessage`
  birikimi) yönetmek, mesaj boyutu sınırını (`MaxMessageBytes = 4 MB`) uygulamak, JSON/binary
  gönderim ve düzgün kapanış (`CloseAsync`) sağlamak.
- **Üstlenmediği:** SSE/WebSocket üzerinden taşınan verinin ANLAMI — hangi event tipinin ne
  içerdiği, tool çağrısı mı yoksa ses baytı mı olduğu tamamen çağıran endpoint'in sorumluluğunda.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- [ChatAndRealtime.md](../Endpoints/ChatAndRealtime.md), [AdminAndHitl.md](../Endpoints/AdminAndHitl.md) —
  `SseWriter`/`SseForwarder`'ın asıl kullanıcıları.
- `IBrowserChannel` (`CustomerSupportBot.Application.Ports.Outbound`) — `WebSocketBrowserChannel`'ın
  implemente ettiği port; `IRealtimeBridge`/`IRealtimeNativeBridge` bu soyutlamaya bağımlıdır,
  somut WebSocket tipine değil.
- [ChatEventOrchestrator](../Services/ChatEventOrchestrator.md) — `SseForwarder`'ı kalıcı
  `/chat/events/{sessionId}` bağlantısında kullanır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

- **`SseWriter` internal, `SseForwarder` public:** `SseWriter` yalnızca tek satırlık çerçeveleme
  mantığı taşır ve API projesi dışına sızmasına gerek yok; `SseForwarder` ise eşzamanlılık
  garantisi sağladığı için endpoint'lerin doğrudan kullandığı asıl arayüzdür.
- **`SemaphoreSlim` neden gerekli:** SSE akışı sırasında iki farklı kaynak (ana workflow akışı ve
  HITL abonelik callback'i) aynı `HttpResponse.Body`'ye eşzamanlı yazabilir; korumasız yazma,
  SSE çerçevelerinin birbirine karışmasına (bozuk JSON) yol açabilirdi.
- **`WebSocketBrowserChannel.MaxMessageBytes` sınırı:** `CancellationToken` bile olsa, parçaları
  hiç bitirmeyen (`EndOfMessage` göndermeyen) kötü niyetli/bozuk bir istemci sınırsız bellek
  tüketebilirdi. Sınır aşıldığında döngü **hemen** kırılır ve soket kapatılır — parçaların
  bitmesini beklemek, sınırı aşan bir istemcinin bağlantıyı süresiz açık tutmasına izin verirdi
  (bu, geliştirme sürecinde test yazılırken yakalanıp düzeltilmiş gerçek bir hataydı).
- **4 MB sınırı seçimi:** gerçek ses akışı parçaları (mikrofon chunk'ları) birkaç KB'lik ayrık
  mesajlardır; 4 MB bu akışı hiçbir zaman sınırlamaz ama kötüye kullanımı engeller.

## 6. Metotlar / Üyeler

### `SseWriter` (internal static)

| Üye | Açıklama |
|---|---|
| `WriteHeaders(HttpResponse)` | `Content-Type: text/event-stream` ve buffering-kapatma header'larını yazar. |
| `WriteEventAsync(HttpResponse, string eventType, object? data, CancellationToken)` | Tek bir SSE olayını JSON'a serileştirip yazar ve flush eder. |

### `SseForwarder : IDisposable`

| Üye | Açıklama |
|---|---|
| `SseForwarder(HttpResponse, CancellationToken)` | Constructor. |
| `WriteAsync(string eventType, object? data)` | Thread-safe SSE yazımı; iptal/hata durumlarını sessizce yutar. |
| `WriteDoneAsync(string sessionId)` | `done` event'i yazar. |
| `WriteErrorAsync(string message)` | `error` event'i yazar. |
| `WriteSessionAsync(string sessionId)` | `session` event'i yazar (akışın ilk olayı). |
| `Dispose()` | İç `SemaphoreSlim`'i serbest bırakır. |

### `WebSocketBrowserChannel : IBrowserChannel` (internal sealed)

| Üye | Açıklama |
|---|---|
| `WebSocketBrowserChannel(WebSocket)` | Constructor. |
| `IsOpen` | Soketin açık olup olmadığı. |
| `ReceiveMessagesAsync(CancellationToken)` | `IAsyncEnumerable<BrowserMessage>` — parçaları biriktirip tam mesajlar üretir; boyut sınırını aşan mesajda soketi `MessageTooBig` ile kapatıp akışı sonlandırır. |
| `SendJsonAsync(object, CancellationToken)` | JSON'ı UTF-8 metin olarak gönderir. |
| `SendBinaryAsync(byte[], CancellationToken)` | Ham baytları binary olarak gönderir. |
| `CloseAsync(string reason, CancellationToken)` | Soketi normal kapanışla (best-effort) kapatır. |

## 7. Bağımlılıklar

- `SseForwarder` → `SseWriter` (statik çağrı).
- `WebSocketBrowserChannel` → yalnızca `System.Net.WebSockets.WebSocket` (constructor'dan alınır,
  `RealtimeEndpoints` tarafından `AcceptWebSocketAsync()` sonucu oluşturulur).

## Bağlantılar

- [../README.md](../README.md) — Katman indeksi
- [Endpoints/ChatAndRealtime](../Endpoints/ChatAndRealtime.md)

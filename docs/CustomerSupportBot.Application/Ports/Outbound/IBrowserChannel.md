# IBrowserChannel (+ BrowserMessage, BrowserMessageKind)

**Kaynak:** `Ports/Outbound/IBrowserChannel.cs`
**Implementasyon:** [`WebSocketBrowserChannel`](../../../CustomerSupportBot.Api/Infrastructure/WebSocketBrowserChannel.md)

## 1. Ne İşe Yarar

Tarayıcı ile çift yönlü mesajlaşma kanalı için secondary port. WebSocket framing, JSON
serileştirme ve bağlantı durum yönetimi bu port'un arkasında gizlenir.

## 2. Hangi Amaçla Kullanılır

Sesli (realtime) chat WebSocket bağlantısı bu port üzerinden tarayıcıya ses/metin/JSON
gönderir ve tarayıcıdan gelen mesajları (ses chunk'ları, kontrol komutları) okur.

## 3. Sorumlulukları

- **Üstlendiği:** Mesaj gönderme (JSON/binary), mesaj alma (akış olarak), bağlantı durumu ve
  kapatma.
- **Üstlenmediği:** WebSocket protokolünün ASP.NET Core seviyesindeki detayları (`HttpContext`,
  middleware) — bunlar `Api` katmanındaki implementasyonda kalır.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Api/Infrastructure/WebSocketBrowserChannel` implemente eder — `System.Net.WebSockets.WebSocket`'i
sarar; `MaxMessageBytes` (4 MB) sınırını aşan mesajlarda bağlantıyı
`WebSocketCloseStatus.MessageTooBig` ile kapatır (DoS koruması).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Application/realtime servisleri ham `WebSocket` API'sinin (frame biriktirme,
`EndOfMessage` döngüsü, close handshake) karmaşıklığıyla uğraşmasın diye bu port var — Api
katmanındaki adaptör bu karmaşıklığı bir kez çözer.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `bool IsOpen { get; }` | Bağlantı açık mı. |
| `IAsyncEnumerable<BrowserMessage> ReceiveMessagesAsync(CancellationToken ct)` | Gelen mesajları akış olarak okur. |
| `Task SendJsonAsync(object payload, CancellationToken ct)` | JSON payload gönderir. |
| `Task SendBinaryAsync(byte[] data, CancellationToken ct)` | Ham binary veri (ses) gönderir. |
| `Task CloseAsync(string reason, CancellationToken ct)` | Bağlantıyı kapatır. |

**`BrowserMessage(BrowserMessageKind Kind, byte[]? Data)`** — `AsText()` yardımcı metodu,
`Kind == Text` ise veriyi UTF-8 string'e çevirir.

**`BrowserMessageKind`** enum: `Text`, `Binary`, `Closed`.

## 7. Bağımlılıklar

Yok — port arayüzü ve veri tipleri bağımlılıksızdır.

# BrowserMessageKind

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/IBrowserChannel.cs`
- **Tür:** `public  enum`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound`

## Ne işe yarar?

`BrowserMessageKind`, <summary> Tarayıcı ile çift yönlü mesajlaşma kanalı için secondary (driven) port. WebSocket framing, JSON serileştirme ve bağlantı durum yönetimi bu port'un arkasında gizlenir. </summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`BrowserMessageKind`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Metotlar ve İç Çalışma Mantıkları

### `BrowserMessage`
```csharp
public sealed record BrowserMessage(BrowserMessageKind Kind, byte[]? Data)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `AsText`
```csharp
public string AsText()
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`

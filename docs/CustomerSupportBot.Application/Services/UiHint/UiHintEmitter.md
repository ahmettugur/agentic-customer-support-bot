# UiHintEmitter

- **Kaynak:** `CustomerSupportBot.Application/Services/UiHint/UiHintEmitter.cs`
- **Tür:** `public sealed class : IUiHintEmitter`
- **Namespace:** `CustomerSupportBot.Application.Services.UiHint`

## Ne işe yarar?

`UiHintEmitter`, <summary> Session ID tabanlı UI hint deposu. IApprovalContextAccessor üzerinden session ID okur — SDK içinden de güvenilir çalışır. </summary> <summary> Event'i ürettiği anda ambient bağlamdaki "şu an çalışan ajan" bilgisiyle etiketler.

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`UiHintEmitter`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public UiHintEmitter(IApprovalContextAccessor ctx)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `Emit`
```csharp
public bool Emit(StreamEvent evt)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `DrainPending`
```csharp
public IReadOnlyList<StreamEvent> DrainPending(string sessionId)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IUiHintEmitter`

# StreamEvent

- **Kaynak:** `CustomerSupportBot.Application/Ports/Inbound/StreamEvent.cs`
- **Tür:** `public  record`
- **Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## Ne işe yarar?

`StreamEvent`, Ports/Driving/StreamEvent.cs IChatPort streaming use case output DTO'su ve event tipleri. <summary> Streaming event modeli — use case boundary output. Type: event adı, Data: JSON olarak serileştirilebilir veri. </summary> <summary> Session event payload — SessionId contract'ını typed tutar. ChatPortService ve ChatEndpoints bu tipi kullanarak sessiz failure riskini ortadan kaldırır. </summary> <summary> <see cref="StreamEventTypes.ResponseDelta"/> ve <see cref="StreamEventTypes.ReasoningDelta"/> event'lerinin payload'ı. Önceden anonim <c>new { text = ... }</c> nesneleri kullanılıyordu ve aggregator'lar (ChatPortService, RealtimeBridgeService, WorkflowResponseExtractor) bunu JSON round-trip veya reflection ile okumak zorunda kalıyordu — rename'de sessizce boş string dönerlerdi. Bu record aynı JSON şekli (camelCase → "text") üretir, tipli okumaya izin verir. </summary> <summary> <see cref="StreamEventTypes.ResponseComplete"/> payload'ı — turun <b>kanonik</b> yanıt metni.

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`StreamEvent`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Metotlar ve İç Çalışma Mantıkları

### `SessionEventPayload`
```csharp
public sealed record SessionEventPayload(string SessionId)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `TextDeltaPayload`
```csharp
public sealed record TextDeltaPayload(string Text)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `ResponseCompletePayload`
```csharp
public sealed record ResponseCompletePayload(
    string Text,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`

# RealtimeToolResult

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/AI/RealtimeModels.cs`
- **Tür:** `public sealed record`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound.AI`

## Ne işe yarar?

`RealtimeToolResult`, Application/Ports/Driven/AI/RealtimeModels.cs IRealtimeVoiceTransport port'u için domain-nötr veri modelleri. OpenAI protokolüne özgü tipler (JsonNode, base64, event string'leri) burada yoktur.

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`RealtimeToolResult`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Metotlar ve İç Çalışma Mantıkları

### `RealtimeServerEvent`
```csharp
public sealed record RealtimeServerEvent(RealtimeServerEventType EventType)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Özellikler/Properties

- `Transcript` (`string?`): İlgili veriyi temsil eden özellik.
- `AudioDelta` (`byte[]?`): İlgili veriyi temsil eden özellik.
- `TextDelta` (`string?`): İlgili veriyi temsil eden özellik.
- `FullText` (`string?`): İlgili veriyi temsil eden özellik.
- `ErrorMessage` (`string?`): İlgili veriyi temsil eden özellik.
- `ToolCallId` (`string?`): İlgili veriyi temsil eden özellik.
- `ToolName` (`string?`): İlgili veriyi temsil eden özellik.
- `ToolArguments` (`string?`): İlgili veriyi temsil eden özellik.

## Bağımlılıklar

- `CustomerSupportBot.Domain`

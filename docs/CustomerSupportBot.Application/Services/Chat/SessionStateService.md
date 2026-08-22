# SessionStateService

- **Kaynak:** `CustomerSupportBot.Application/Services/Chat/SessionStateService.cs`
- **Tür:** `public sealed class`
- **Namespace:** `CustomerSupportBot.Application.Services.Chat`

## Ne işe yarar?

`SessionStateService`, Application/Services/SessionStateService.cs Session state yönetimi — sentiment güncellemesi, intent güncellemesi, sentiment alert kontrolü. ChatStreamOrchestrator (Api) bu servisi kullanır; iş mantığı Application katmanında kalır. <summary> Oturum durumu iş mantığı — sentiment güncelleme, intent yönetimi, alert kontrolü. Transport katmanından (SSE, WebSocket) bağımsızdır. </summary> <summary> Konuşmayı (exchange) session history'ye kaydeder, turun state çıkarımını tetikler ve ChatBridge'e bildirir.

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`SessionStateService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public SessionStateService(ISessionManager sessionManager,
        ILogger<SessionStateService> logger)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `PersistExchangeAsync`
```csharp
public async Task PersistExchangeAsync(
        string sessionId,
        string query,
        string response,
        IChatBridge chatBridge,
        TurnSignals? signals = null,
        CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `CheckSentimentAlert`
```csharp
public SentimentAlertResult CheckSentimentAlert(AgentSession session)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Özellikler/Properties

- `Sentiment` (`string?`): İlgili veriyi temsil eden özellik.
- `Score` (`double`): İlgili veriyi temsil eden özellik.
- `ConsecutiveNegativeTurns` (`int`): İlgili veriyi temsil eden özellik.
- `SessionId` (`string`): İlgili veriyi temsil eden özellik.
- `ShouldAlert` (`bool`): İlgili veriyi temsil eden özellik.

## Bağımlılıklar

- `CustomerSupportBot.Domain`

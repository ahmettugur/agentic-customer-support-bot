# RealtimeBridgeService

- **Kaynak:** `CustomerSupportBot.Application/Services/Realtime/RealtimeBridgeService.cs`
- **Tür:** `public sealed class : IRealtimeBridge`
- **Namespace:** `CustomerSupportBot.Application.Services.Realtime`

## Ne işe yarar?

`RealtimeBridgeService`, <summary> Köprü modu realtime oturumu — browser kanalı ↔ agent pipeline orkestrasyonu. Her bağlantı için ayrı bir Scoped instance oluşturulur. </summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`RealtimeBridgeService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public RealtimeBridgeService(IRealtimeVoiceTransport client,
        IAgentTeamPort team,
        ISessionManager sessionManager,
        IReasoningPort reasoningService,
        IApprovalContextAccessor approvalContext,
        IChatBridge chatBridge,
        IInputGuard inputGuard,
        IAppDistributedLock sessionLock,
        ILogger<RealtimeBridgeService> logger)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `RunAsync`
```csharp
public async Task RunAsync(
        IBrowserChannel channel, string sessionId, string? authenticatedCustomerId, CancellationToken ct)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `HandleUserTranscriptAsync`
```csharp
internal async Task HandleUserTranscriptAsync(
        IBrowserChannel channel,
        AgentSession session,
        string transcript,
        CancellationToken ct)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IRealtimeBridge`

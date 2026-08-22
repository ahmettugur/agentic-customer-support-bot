# ChatSessionPortService

- **Kaynak:** `CustomerSupportBot.Application/Services/Chat/ChatSessionPortService.cs`
- **Tür:** `public sealed class : IChatSessionPort`
- **Namespace:** `CustomerSupportBot.Application.Services.Chat`

## Ne işe yarar?

`ChatSessionPortService`, Application/Services/ChatSessionPortService.cs DRIVING PORT IMPL — IChatSessionPort → canlı takeover ve replan orkestrasyonu.

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ChatSessionPortService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public ChatSessionPortService(IChatModeRegistry chatModes,
        IChatBridge chatBridge,
        ISessionManager sessions,
        IEscalationSink escalations,
        IHumanAgentRegistry agents,
        IReplanService replanService)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `GetActive`
```csharp
public IReadOnlyList<ChatSessionState> GetActive()
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetStateOrDefault`
```csharp
public ChatSessionState GetStateOrDefault(string sessionId)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetHistory`
```csharp
public IReadOnlyList<ChatBridgeMessage> GetHistory(string sessionId, int take = 50)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetSentimentAsync`
```csharp
public async Task<ChatSessionSentimentSnapshot?> GetSentimentAsync(string sessionId, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `PublishSystemMessage`
```csharp
public void PublishSystemMessage(string sessionId, string text)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `TakeOver`
```csharp
public ChatSessionTakeoverResult TakeOver(string sessionId, string humanAgent, string? agentId = null)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `Release`
```csharp
public ChatSessionReleaseResult Release(string sessionId, string? agentId = null)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `SendAdminMessageAsync`
```csharp
public async Task<ChatSessionMessageResult> SendAdminMessageAsync(
        string sessionId, string humanAgent, string text, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `ReplanSessionAsync`
```csharp
public async Task<ChatSessionReplanResult> ReplanSessionAsync(
        string sessionId, string requestedBy, string? note, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `ReplanEscalationAsync`
```csharp
public async Task<ChatSessionReplanResult> ReplanEscalationAsync(
        string escalationId, string requestedBy, string? note, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `SubscribeToAdminAsync`
```csharp
public IAsyncEnumerable<ChatBridgeMessage> SubscribeToAdminAsync(string sessionId, CancellationToken ct)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `SubscribeToUserAsync`
```csharp
public IAsyncEnumerable<ChatBridgeMessage> SubscribeToUserAsync(string sessionId, CancellationToken ct)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetOpenEscalations`
```csharp
public IReadOnlyList<EscalationRequest> GetOpenEscalations()
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `DismissOrphanedEscalations`
```csharp
public int DismissOrphanedEscalations(string sessionId)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IChatSessionPort`

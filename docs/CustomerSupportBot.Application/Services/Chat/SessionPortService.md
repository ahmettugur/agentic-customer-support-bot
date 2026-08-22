# SessionPortService

- **Kaynak:** `CustomerSupportBot.Application/Services/Chat/SessionPortService.cs`
- **Tür:** `public sealed class : ISessionPort`
- **Namespace:** `CustomerSupportBot.Application.Services.Chat`

## Ne işe yarar?

`SessionPortService`, Application/Services/SessionPortService.cs DRIVING PORT IMPL — ISessionPort → Driven portları orkestrasyonla kullanır. <summary> Oturum yönetimi driving port implementasyonu. HTTP adaptörü (SessionEndpoints) bu sınıfı ISessionPort olarak kullanır. </summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`SessionPortService`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public SessionPortService(ISessionManager sessions,
        ILogger<SessionPortService> logger)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `GetOrCreateSessionAsync`
```csharp
public async Task<AgentSession> GetOrCreateSessionAsync(string? sessionId, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetSessionAsync`
```csharp
public Task<AgentSession?> GetSessionAsync(string sessionId, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `UpdateSessionAsync`
```csharp
public async Task UpdateSessionAsync(AgentSession session, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetAllSessionsAsync`
```csharp
public async Task<IReadOnlyList<SessionInfo>> GetAllSessionsAsync(
        string? forCustomerId = null, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `GetHistoryAsync`
```csharp
public Task<List<ConversationMessage>> GetHistoryAsync(string sessionId, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `AddExchangeAsync`
```csharp
public async Task AddExchangeAsync(string sessionId, string userMessage, string botResponse, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `ExtractAndUpdateStateAsync`
```csharp
public Task ExtractAndUpdateStateAsync(string sessionId, string userMessage, string botResponse, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

### `MutateStateAsync`
```csharp
public async Task MutateStateAsync(string sessionId, Action<SessionState> mutator, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `ISessionPort`

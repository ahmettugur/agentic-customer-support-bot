# Infrastructure

**Klasör:** `Infrastructure/`

HTTP boundary'sini koruyan altyapı sınıfları — exception handler, SSE writer, WebSocket adapter, YAML loader.

| Dosya | Sorumluluk |
|---|---|
| `DomainExceptionHandler.cs` | DomainException → HTTP status + ProblemDetails |
| `SseWriter.cs` | Server-Sent Events format |
| `SseForwarder.cs` | Thread-safe SSE wrapper |
| `WebSocketBrowserChannel.cs` | WebSocket ↔ IBrowserChannel port |
| `ScenarioLoader.cs` | YAML deserialization helper |

---

## DomainExceptionHandler

ASP.NET Core 8+ `IExceptionHandler` interface'ini implement eder. Middleware pipeline'da en başta çalışır.

### Mapping tablosu

| Domain Exception | HTTP Status | Log Level | Detay yansıt? |
|---|---|---|---|
| `EntityNotFoundException` | `404 Not Found` | Information | ✅ Evet (entityType, entityId) |
| `ConcurrencyConflictException` | `409 Conflict` | Warning | ✅ Evet |
| `ExternalServiceException` | `503 Service Unavailable` | Error | ❌ Hayır (service adı verir) |
| `PersistenceException` | `503 Service Unavailable` | Error | ❌ Hayır |
| Diğer `DomainException` | `400 Bad Request` | Warning | ✅ Evet |
| `Exception` (yakalanmamış) | `500 Internal Server Error` | Error | ❌ Hayır |

### ProblemDetails formatı

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.4",
  "title": "Resource not found",
  "status": 404,
  "detail": "Sipariş 5 bulunamadı",
  "extensions": {
    "code": "ORDER_NOT_FOUND",
    "entityType": "Order",
    "entityId": "5",
    "traceId": "00-abc..."
  }
}
```

RFC 7807 standardı — modern istemciler bunu otomatik parse eder.

### `HasStarted` koruması

```csharp
if (httpContext.Response.HasStarted)
{
    _logger.LogWarning(ex, "Response already started — cannot translate exception");
    return false;   // Diğer middleware'e devret
}
```

SSE veya WebSocket akışı **zaten başlamışsa** response'a yazamayız. Bu durumda exception loglanır ama HTTP'ye yansımaz — istemci stream'de hata fark eder.

### Service identifier yansıması

`ExternalServiceException` için sadece **servis adı** verilir:

```json
{
  "status": 503,
  "title": "External service unavailable",
  "detail": "Yapay zeka servisi şu an kullanılamıyor.",
  "extensions": {
    "service": "AI"
  }
}
```

Düşük seviye detay (stack trace, provider error code) **production'da gizli** — log'da tutulur.

---

## SseWriter

Server-Sent Events protokolünü implement eder. Tek istemciye sürekli event akışı.

### SSE header'ları

```csharp
response.Headers["Content-Type"]     = "text/event-stream";
response.Headers["Cache-Control"]    = "no-cache, no-transform";
response.Headers["X-Accel-Buffering"] = "no";   // nginx buffer'ı kapat
response.Headers["Connection"]       = "keep-alive";
```

`X-Accel-Buffering: no` reverse proxy'lerde (nginx) bufferı kapatır — event'ler anında istemciye akar.

### Event formatı

```
event: TYPE\n
data: JSON_PAYLOAD\n
\n
```

İki newline event sonunu işaretler. Boş satır olmadan istemci event'i bitmiş saymaz.

### `WriteEventAsync`

```csharp
public async Task WriteEventAsync(string eventType, object data, CancellationToken ct)
{
    var json = JsonSerializer.Serialize(data, _jsonOptions);
    var bytes = Encoding.UTF8.GetBytes($"event: {eventType}\ndata: {json}\n\n");
    await _response.Body.WriteAsync(bytes, ct);
    await _response.Body.FlushAsync(ct);   // Buffer'a yazmayı bekleme, hemen gönder
}
```

`Flush` kritik — yoksa event'ler buffer'da birikir, istemciye geç ulaşır.

### JSON serialization

```csharp
_jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};
```

`UnsafeRelaxedJsonEscaping` Türkçe karakterleri escape etmez (`ç`, `ğ`, vb.). UTF-8 native gönderim.

### Anonymous payload helper

```csharp
public static string? GetTextFromAnon(object obj)
{
    var prop = obj.GetType().GetProperty("text");
    return prop?.GetValue(obj) as string;
}
```

Anonymous type'lardan reflection ile "text" property çıkarır — endpoint kodunda DTOcum oluşturmak yerine inline anon kullanmaya izin verir.

---

## SseForwarder

`SseWriter`'ın thread-safe sarmalı. Birden fazla kaynak (HITL events + bot response) aynı stream'e yazıyorsa lock gerek.

```csharp
public sealed class SseForwarder
{
    private readonly SseWriter _writer;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task WriteAsync(string eventType, object data, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await _writer.WriteEventAsync(eventType, data, ct);
        }
        catch (OperationCanceledException) { /* client disconnect */ }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SSE write failed");
        }
        finally
        {
            _lock.Release();
        }
    }
}
```

### Yardımcı metodlar

| Metod | Event tipi | İçerik |
|---|---|---|
| `WriteSessionAsync(sessionId)` | `session` | `{ sessionId }` |
| `WriteAsync(type, data)` | Custom | Custom |
| `WriteDoneAsync(sessionId)` | `done` | `{ sessionId }` — stream sonu |
| `WriteErrorAsync(message)` | `error` | `{ message }` |

### Exception swallow

Client disconnect olursa `OperationCanceledException` veya `IOException` fırlar. Bu **normal akış** — exception olarak yansıtılmaz, sadece debug log.

---

## WebSocketBrowserChannel

`IBrowserChannel` driven port'unun WebSocket implementasyonu. Browser ↔ Application veri akışını WS frame'lerine çevirir.

### `IBrowserChannel` arayüzü

```csharp
public interface IBrowserChannel
{
    bool IsOpen { get; }
    IAsyncEnumerable<BrowserMessage> ReceiveAsync(CancellationToken ct);
    Task SendJsonAsync<T>(T payload, CancellationToken ct);
    Task SendBinaryAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct);
    Task CloseAsync(string reason, CancellationToken ct);
}

public sealed record BrowserMessage(BrowserMessageKind Kind, ReadOnlyMemory<byte> Payload);
public enum BrowserMessageKind { Binary, Text, Closed }
```

### Frame okuma (binary protocol)

```csharp
public async IAsyncEnumerable<BrowserMessage> ReceiveAsync([EnumeratorCancellation] CancellationToken ct)
{
    var buffer = new byte[16 * 1024];
    var ms = new MemoryStream();

    while (_socket.State == WebSocketState.Open)
    {
        var result = await _socket.ReceiveAsync(buffer, ct);
        if (result.MessageType == WebSocketMessageType.Close)
        {
            yield return new BrowserMessage(BrowserMessageKind.Closed, ReadOnlyMemory<byte>.Empty);
            yield break;
        }

        ms.Write(buffer, 0, result.Count);
        if (result.EndOfMessage)
        {
            var kind = result.MessageType == WebSocketMessageType.Binary
                ? BrowserMessageKind.Binary
                : BrowserMessageKind.Text;
            yield return new BrowserMessage(kind, ms.ToArray());
            ms.SetLength(0);
        }
    }
}
```

- 16 KB buffer her receive call'da
- `EndOfMessage` false ise tek frame'in parçası — MemoryStream'de biriktir
- `EndOfMessage` true → tam mesaj yield et, stream'i sıfırla
- Close frame → `Closed` mesajı yield et, enumeration biter

### Send

```csharp
public async Task SendJsonAsync<T>(T payload, CancellationToken ct)
{
    if (!IsOpen) return;
    var json = JsonSerializer.Serialize(payload, _jsonOptions);
    var bytes = Encoding.UTF8.GetBytes(json);
    await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
}

public async Task SendBinaryAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct)
{
    if (!IsOpen) return;
    await _socket.SendAsync(bytes, WebSocketMessageType.Binary, endOfMessage: true, ct);
}
```

`endOfMessage: true` — tek frame'de bütün mesaj gönderilir (fragmentation yok).

### Kullanım

`RealtimeBridgeService` ve `RealtimeNativeService` (Application) bu adapter'ı kullanır:

```
Browser ──WS──→ Api/Realtime endpoint
                   ↓
                WebSocketBrowserChannel oluştur
                   ↓
                IRealtimeBridge.RunAsync(channel, sessionId)
                   ├── channel.ReceiveAsync()           ← mikrofon
                   ├── channel.SendBinaryAsync(audio)   ← asistan sesi
                   └── channel.SendJsonAsync(event)     ← transcript, durum
```

---

## ScenarioLoader

YAML eval senaryolarını okur — `evaluation-scenarios.yaml`.

```csharp
public static class ScenarioLoader
{
    private static readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static ScenarioFile Load(string path)
    {
        var yaml = File.ReadAllText(path);
        return _deserializer.Deserialize<ScenarioFile>(yaml);
    }
}
```

### Naming convention

YAML'da `snake_case`, C#'ta `PascalCase`:

```yaml
scenarios:
  - id: order-inquiry-basic
    user_query: "5 nerede"
    expected_intent: OrderInquiry
    expected_agents: [PlanningAgent, OrderAgent, ResponseAgent]
    known_failure_modes: []
```

```csharp
public sealed class Scenario
{
    public string Id { get; set; }
    public string UserQuery { get; set; }        // snake → Pascal
    public string ExpectedIntent { get; set; }
    public List<string> ExpectedAgents { get; set; }
    public List<string> KnownFailureModes { get; set; }
}
```

`IgnoreUnmatchedProperties` — YAML'da fazla alan varsa hata fırlatmaz (forward compat).

### Kullanan endpoint

`/eval/scenarios` — senaryo listesi  
`/eval/run` — tüm/N senaryo çalıştır  
`/eval/run/{id}` — tek senaryo

Detay: [Endpoints-Observability.md](Endpoints-Observability.md).

---

## Bağlantılar

- [Services.md](Services.md) — ChatEventOrchestrator SSE'yi nasıl kullanır
- [Endpoints-Chat.md](Endpoints-Chat.md) — `/chat/stream` SSE örneği
- [Adapters.AI Realtime](../adapters-ai/Realtime.md) — WebSocket OpenAI tarafı
- [Domain Exceptions](../domain/Exceptions.md) — DomainException hiyerarşisi

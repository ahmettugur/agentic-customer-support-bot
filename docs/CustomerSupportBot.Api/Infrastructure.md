# Infrastructure

**Klasör:** `Infrastructure/`

HTTP boundary'sini koruyan altyapı sınıfları — exception handler, SSE writer, WebSocket adapter, YAML loader.

| Dosya | Sorumluluk |
|---|---|
| `DomainExceptionHandler.cs` | DomainException → HTTP status + ProblemDetails |
| `SseWriter.cs` | Server-Sent Events format (static helper) |
| `SseForwarder.cs` | Thread-safe SSE wrapper (disposable) |
| `WebSocketBrowserChannel.cs` | WebSocket ↔ IBrowserChannel port |
| `ScenarioLoader.cs` | YAML deserialization helper |

---

## DomainExceptionHandler

ASP.NET Core `IExceptionHandler` interface'ini implement eder (`internal sealed`). Middleware pipeline'da en başta çalışır.

### Mapping tablosu

| Domain Exception | HTTP Status | Log Level | Detay yansıt? |
|---|---|---|---|
| `EntityNotFoundException` | `404 Not Found` | Information | ✅ Evet (entityType, entityId) |
| `ConcurrencyConflictException` | `409 Conflict` | Warning | ✅ Evet |
| `ExternalServiceException` | `503 Service Unavailable` | Error | ❌ Hayır (sadece service adı) |
| `PersistenceException` | `503 Service Unavailable` | Error | ❌ Hayır |
| Diğer `DomainException` | `400 Bad Request` | Warning | ✅ Evet |
| `Exception` değil (yakalanmamış) | — | — | `false` döner, 500 handler devreye girer |

### ProblemDetails formatı

```json
{
  "status": 404,
  "title": "ENTITY_NOT_FOUND",
  "detail": "Order '5' bulunamadı.",
  "extensions": {
    "code": "ENTITY_NOT_FOUND",
    "entityType": "Order",
    "entityId": "5"
  }
}
```

`ExternalServiceException` için:

```json
{
  "status": 503,
  "title": "EXTERNAL_SERVICE_UNAVAILABLE",
  "detail": "Servis şu anda isteği işleyemiyor; lütfen daha sonra tekrar deneyin.",
  "extensions": {
    "code": "EXTERNAL_SERVICE_UNAVAILABLE",
    "service": "AI"
  }
}
```

5xx'te iç bağlam (stack trace, provider error code) **production'da gizli** — log'da tutulur.

### `HasStarted` koruması

```csharp
if (httpContext.Response.HasStarted)
    return false;
```

SSE veya WebSocket akışı **zaten başlamışsa** response'a yazamayız. Exception loglanır ama HTTP'ye yansımaz.

---

## SseWriter

Server-Sent Events protokolünü implement eder (`internal static`). Tek istemciye sürekli event akışı.

### SSE header'ları

```csharp
response.Headers["Content-Type"]      = "text/event-stream";
response.Headers["Cache-Control"]     = "no-cache, no-transform";
response.Headers["X-Accel-Buffering"] = "no";   // nginx buffer'ı kapat
response.Headers["Connection"]        = "keep-alive";
```

### Event formatı

```
event: TYPE\n
data: JSON_PAYLOAD\n
\n
```

### `WriteEventAsync`

```csharp
public static async Task WriteEventAsync(
    HttpResponse response,
    string eventType,
    object? data,
    CancellationToken ct)
{
    if (ct.IsCancellationRequested) return;

    var json = data != null ? JsonSerializer.Serialize(data, SseJsonOptions) : "{}";
    var sb = new StringBuilder();
    sb.Append("event: ").Append(eventType).Append('\n');
    sb.Append("data: ").Append(json).Append("\n\n");

    await response.WriteAsync(sb.ToString(), ct);
    await response.Body.FlushAsync(ct);
}
```

`Flush` kritik — yoksa event'ler buffer'da birikir, istemciye geç ulaşır.

### JSON serialization

```csharp
private static readonly JsonSerializerOptions SseJsonOptions = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};
```

`UnsafeRelaxedJsonEscaping` — Türkçe karakterleri escape etmez (`ç`, `ğ`, vb.).

### Anonymous payload helper

```csharp
public static string GetTextFromAnon(object data)
{
    var prop = data.GetType().GetProperty("text");
    return prop?.GetValue(data) as string ?? "";
}
```

Reflection ile anonymous type'lardan "text" property okur.

---

## SseForwarder

`SseWriter`'ın thread-safe sarmalı (`public sealed`, `IDisposable`). Birden fazla kaynak (HITL events + bot response) aynı stream'e yazıyorsa lock gerek.

### Constructor

```csharp
public SseForwarder(HttpResponse response, CancellationToken cancellationToken)
```

`SemaphoreSlim(1,1)` ile concurrent write'lar serialize edilir.

### Metodlar

| Metod | Event tipi | İçerik |
|---|---|---|
| `WriteAsync(type, data)` | Custom | Custom |
| `WriteSessionAsync(sessionId)` | `session` | `{ sessionId }` |
| `WriteDoneAsync(sessionId)` | `done` | `{ sessionId }` — stream sonu |
| `WriteErrorAsync(message)` | `error` | `{ message }` |

### Exception swallow

```csharp
catch (OperationCanceledException) { /* Client disconnected */ }
catch (Exception) { /* Write failures are expected in SSE */ }
```

Client disconnect olursa normal akış — exception olarak yansıtılmaz.

### `Dispose`

```csharp
public void Dispose()
{
    if (_disposed) return;
    _disposed = true;
    _lock.Dispose();
}
```

`using` ile kullanılır — endpoint sona erince semaphore release edilir.

---

## WebSocketBrowserChannel

`IBrowserChannel` driven port'unun WebSocket implementasyonu (`internal sealed`). Browser ↔ Application veri akışını WS frame'lerine çevirir.

### `IBrowserChannel` arayüzü

```csharp
bool IsOpen { get; }
IAsyncEnumerable<BrowserMessage> ReceiveMessagesAsync(CancellationToken ct);
Task SendJsonAsync(object payload, CancellationToken ct);
Task SendBinaryAsync(byte[] data, CancellationToken ct);
Task CloseAsync(string reason, CancellationToken ct);
```

### Frame okuma

```csharp
while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
{
    ms.SetLength(0);
    WebSocketReceiveResult result;
    var tooLarge = false;
    do
    {
        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
        if (result.MessageType == WebSocketMessageType.Close)
        {
            yield return new BrowserMessage(BrowserMessageKind.Closed, null);
            yield break;
        }

        if (ms.Length + result.Count > MaxMessageBytes) { tooLarge = true; break; }
        ms.Write(buffer, 0, result.Count);
    }
    while (!result.EndOfMessage);

    if (tooLarge)
    {
        await _ws.CloseAsync(WebSocketCloseStatus.MessageTooBig, "...", ct);
        yield return new BrowserMessage(BrowserMessageKind.Closed, null);
        yield break;
    }

    yield return new BrowserMessage(kind, ms.ToArray());
}
```

- 16 KB buffer her receive call'da
- `EndOfMessage` false ise tek frame'in parçası — MemoryStream'de biriktir
- Close frame → `Closed` mesajı yield et, enumeration biter

**`MaxMessageBytes` = 4 MB.** Parçaları hiç bitirmeyen (kasıtlı ya da bozuk) bir istemci sınır
olmadan sunucu belleğini tekli bir bağlantıdan sınırsız büyütebilirdi. Sınır aşılır aşılmaz
döngüden **hemen** çıkılır — parçaların bitmesini (`EndOfMessage`) beklemek, bellek büyümesi
dursa bile kötü niyetli bir istemcinin bağlantıyı süresiz meşgul tutmasına izin verirdi.
Bağlantı `WebSocketCloseStatus.MessageTooBig` ile kapatılır. Sesli akıştaki gerçek parçalar
(mikrofon chunk'ları) birkaç KB'lik ayrık mesajlardır; 4 MB bu akışı asla sınırlamaz.

### Send

```csharp
public async Task SendJsonAsync(object payload, CancellationToken ct)
{
    if (_ws.State != WebSocketState.Open) return;
    var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts);
    await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
}

public async Task SendBinaryAsync(byte[] data, CancellationToken ct)
{
    if (_ws.State != WebSocketState.Open) return;
    await _ws.SendAsync(data, WebSocketMessageType.Binary, true, ct);
}
```

`endOfMessage: true` — tek frame'de bütün mesaj, fragmentation yok.

### Close

```csharp
public async Task CloseAsync(string reason, CancellationToken ct)
{
    if (_ws.State != WebSocketState.Open) return;
    try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, ct); }
    catch { /* best effort */ }
}
```

### Kullanım

`RealtimeEndpoints` bu adapter'ı oluşturur; `IRealtimeBridge` veya `IRealtimeNativeBridge` (Application) bunu tüketir:

```
Browser ──WS──→ Api/Realtime endpoint
                   ↓
                WebSocketBrowserChannel oluştur
                   ↓
                IRealtimeBridge.RunAsync(channel, sessionId, ct)
```

---

## ScenarioLoader

YAML eval senaryolarını okur — `evaluation-scenarios.yaml`.

```csharp
public static class ScenarioLoader
{
    public static ScenarioFile LoadScenarios(string yamlPath)
    {
        var yaml = File.ReadAllText(yamlPath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        return deserializer.Deserialize<ScenarioFile>(yaml);
    }
}
```

### Naming convention

YAML'da `snake_case` → C#'ta `PascalCase` (UnderscoredNamingConvention):

```yaml
scenarios:
  - id: order-inquiry-basic
    query: "5 nerede"
    expected_intent: OrderInquiry
    expected_agents: [PlanningAgent, OrderAgent]
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
- [Adapters.AI Realtime](../CustomerSupportBot.Adapters.AI/Realtime.md) — WebSocket OpenAI tarafı
- [Domain Exceptions](../CustomerSupportBot.Domain/Exceptions/DomainException.md) — DomainException hiyerarşisi

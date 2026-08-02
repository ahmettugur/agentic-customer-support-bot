# Chat Client'lar

**Dosyalar:**
- `Chat/AiClientFactory.cs` — Provider factory
- `Chat/GeneralChatClientAdapter.cs` — `IGeneralChatClient` impl
- `Chat/ReasoningChatClient.cs` — `IReasoningChatClient` impl

---

## AiClientFactory

**Sorumluluk:** `AiProvider` enum'unu okur, doğru sağlayıcı SDK'sından `IChatClient` üretir.  
**Tür:** `public static class`

### `CreateStandardChatClient`

```csharp
public static IChatClient CreateStandardChatClient(AiOptions options)
```

İki dal:

| Provider | İç implementasyon |
|---|---|
| `OpenAI` | `new OpenAIClient(apiKey).GetChatClient(model).AsIChatClient()` |
| `AzureOpenAI` | `new AzureOpenAIClient(endpoint, apiKey).GetChatClient(deployment).AsIChatClient()` |

`Microsoft.Extensions.AI.IChatClient` ortak arayüz — iki SDK'nın native client'ı bu interface'e adapte edilir.

### `CreateReasoningChatClient`

```csharp
public static ReasoningChatClient CreateReasoningChatClient(
    AiOptions options,
    Func<IChatClient, IChatClient>? decorate = null)
```

Standart client'a ek:
- `ReasoningModel` / `ReasoningDeployment` yoksa standart model/deployment'a düşer
- `decorate` parametresi ile telemetri decorator inject edilebilir; `null` ise iç client doğrudan kullanılır
- Oluşturulan `ReasoningChatClient` içinde `ModelName` ve `ReasoningEffort` saklanır

### `decorate` parametresi

Composition Root telemetri decorator inject etmek için:

```csharp
var reasoningClient = AiClientFactory.CreateReasoningChatClient(
    options,
    decorate: rawClient => new TelemetryChatClient(rawClient, ...));
```

`rawClient` parametresi factory'nin az önce yarattığı provider client'ı; wrapper TelemetryChatClient ile sarmalar. Sonuç `ReasoningChatClient` içinde tutulur.

### Require() helper

```csharp
private static string Require(string? value, string key)
{
    if (!string.IsNullOrWhiteSpace(value))
        return value!;
    throw new InvalidOperationException(
        $"{key} yapılandırması bulunamadı (appsettings.json'da boş veya tanımsız).");
}
```

Eksik config tespiti — `Require(options.OpenAI.ApiKey, "AI:OpenAI:ApiKey")` gibi kullanılır. Startup'ta fail-fast davranışı sağlar.

---

## GeneralChatClientAdapter

**Port:** `IGeneralChatClient`

Genel amaçlı LLM completion — reasoning değil, basit prompt → response.

### Constructor

```csharp
public GeneralChatClientAdapter(IChatClient client)
```

DI tarafından sağlanan `IChatClient` (TelemetryChatClient decorator ile sarılmış) ile oluşturulur.

### `CompleteAsync`

```csharp
public async Task<string> CompleteAsync(
    IReadOnlyList<ConversationMessage> messages,
    CancellationToken ct = default)
{
    var chatMessages = messages.Select(m => new ChatMessage(RoleFor(m.Role), m.Text)).ToList();
    var response = await _client.GetResponseAsync(chatMessages, cancellationToken: ct);
    return response.Text ?? "";
}
```

`IReadOnlyList<ConversationMessage>` alır; domain tiplerini `ChatMessage`'a çevirir; `IChatClient.GetResponseAsync` çağırır.

### Role mapping

`ConversationMessage.Role` (string) → `Microsoft.Extensions.AI.ChatRole`:

| Domain (`ConversationRoles`) | ChatRole |
|---|---|
| `User` | `ChatRole.User` |
| `System` | `ChatRole.System` |
| Diğer (`Assistant` vb.) | `ChatRole.Assistant` |

Bu mapping iki dünya arasında köprü — Application sadece string role kullanır, adapter SDK enum'una çevirir.

### Cancellation davranışı

`OperationCanceledException` özel olarak ele alınır:
- Kullanıcı isteği iptal etti → exception olduğu gibi yükselt (translate etme)
- Aksi halde `ExceptionTranslator.Translate` ile domain exception'a çevir

Bu sayede iptal ile gerçek hata ayırt edilir.

---

## ReasoningChatClient

**Port:** `IReasoningChatClient`

OpenAI o-series modelleri için — derin düşünme modeli wrapper'ı.

### Constructor

```csharp
public ReasoningChatClient(IChatClient client, string modelName, string reasoningEffort)
```

`AiClientFactory.CreateReasoningChatClient` tarafından oluşturulur; doğrudan `new` ile çağrılmaz.

### Property'ler

```csharp
public string ModelName { get; }
public string ReasoningEffort { get; }   // "low", "medium", "high"
```

Constructor'da alınır, kullanıcıya gösterilebilir (admin paneli). `ModelName` gerçek model/deployment adıdır (ReasoningModel veya fallback).

### `CompleteAsync` — non-streaming

```csharp
public async Task<string> CompleteAsync(
    IReadOnlyList<ConversationMessage> messages,
    CancellationToken ct = default)
{
    var chatMessages = Map(messages);
    var options = BuildOptions();
    var response = await _client.GetResponseAsync(chatMessages, options, ct);
    return response.Text ?? "";
}
```

`IReadOnlyList<ConversationMessage>` alır.

### `StreamAsync` — streaming

```csharp
public async IAsyncEnumerable<string> StreamAsync(
    IReadOnlyList<ConversationMessage> messages,
    [EnumeratorCancellation] CancellationToken ct = default)
```

Streaming başlatma bloğu `try/catch` içinde sarılmıştır; ardından `await foreach` ile update'ler yield edilir. Boş/null text update'leri filtrelenir — sadece anlamlı text chunk'ları yield edilir.

### `Map` (internal static)

```csharp
internal static IList<ChatMessage> Map(IReadOnlyList<ConversationMessage> messages)
    => messages.Select(m => new ChatMessage(RoleFor(m.Role), m.Text)).ToList();
```

Test amacıyla `internal` olarak açıktır.

### `BuildOptions`

```csharp
private ChatOptions BuildOptions() => new()
{
    AdditionalProperties = new AdditionalPropertiesDictionary
    {
        [WellKnown.ReasoningEffort.PropertyKey] = ReasoningEffort
    }
};
```

`reasoning_effort` parametresi `AdditionalProperties` üzerinden geçirilir — `Microsoft.Extensions.AI` SDK bunu provider-spesifik field'a çevirir.

- **OpenAI o-series:** Native destekler — `low/medium/high` derin düşünme miktarını ayarlar
- **Azure OpenAI o-series:** OpenAI ile aynı

---

## Reasoning Effort etkisi

| Effort | OpenAI o1 davranışı |
|---|---|
| `low` | Hızlı yanıt, kısa düşünme |
| `medium` | Orta seviye reasoning |
| `high` | Derin reasoning, daha yavaş ama daha doğru |

Maliyet effort'a göre artar — tokens billing'i derin düşünme süresinde de işler. `medium` çoğu senaryo için yeterli.

---

## Streaming detayları

`StreamAsync` neden var?

- Reasoning model'ler **yavaş** — kullanıcıya "yazıyor..." göstermek için streaming şart
- ChatBot UI text'i parça parça gösterir → algılanan latency azalır

Akış:

```
ReasoningAgent (Application)
   ↓ StreamAsync
ReasoningChatClient
   ↓ GetStreamingResponseAsync
Microsoft.Extensions.AI → Provider SDK
   ↓ SSE veya WebSocket
LLM → token by token
   ↓ ChatResponseUpdate (her token bir update)
yield return chunk
   ↓
Application SignalR/WebSocket → Browser
```

---

## Exception handling

Her iki adapter de `ExceptionTranslator` kullanır:

```csharp
catch (Exception ex) when (ex is not OperationCanceledException || ct.IsCancellationRequested is false)
{
    throw ExceptionTranslator.Translate(ex, "context message");
}
```

Bu `when` koşulu gerçek kullanıcı iptalleri (`ct.IsCancellationRequested == true`) için translation yapmayı atlar. Böylece kullanıcı isteği ile gerçek servis hatası ayırt edilir.

- HTTP 429 → `ExternalServiceException("AI", "rate limit")`
- HTTP 401/403 → `ExternalServiceException("AI", "authorization failed")`
- HTTP 5xx → `ExternalServiceException("AI", "service unavailable")`
- Timeout → `ExternalServiceException("AI", "timeout")`

Detay: [ExceptionTranslator.md](ExceptionTranslator.md).

---

## Bağlantılar

- [Options.md](Options.md) — Provider config alanları
- [DependencyInjection.md](DependencyInjection.md) — Composition Root pattern, IChatClient kaydı
- [Adapters.Telemetry/TelemetryChatClient.md](../adapters-telemetry/TelemetryChatClient.md) — decorator

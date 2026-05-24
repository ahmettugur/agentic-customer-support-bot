# Chat Client'lar

**Dosyalar:**
- `Chat/AiClientFactory.cs` — Provider factory
- `Chat/GeneralChatClientAdapter.cs` — `IGeneralChatClient` impl
- `Chat/ReasoningChatClient.cs` — `IReasoningChatClient` impl

---

## AiClientFactory

**Sorumluluk:** `AiProvider` enum'unu okur, doğru sağlayıcı SDK'sından `IChatClient` üretir.

### `CreateStandardChatClient`

```csharp
public static IChatClient CreateStandardChatClient(AiOptions options, ILoggerFactory loggerFactory)
```

Üç dal:

| Provider | İç implementasyon |
|---|---|
| `OpenAI` | `new OpenAIClient(apiKey).AsChatClient(model)` |
| `AzureOpenAI` | `new AzureOpenAIClient(endpoint, apiKey).AsChatClient(deployment)` |
| `Anthropic` | `new AnthropicClient(apiKey).AsChatClient(model, maxTokens)` |

`Microsoft.Extensions.AI.IChatClient` ortak arayüz — üç SDK'nın native client'ı bu interface'e adapte edilir.

### `CreateReasoningChatClient`

```csharp
public static IReasoningChatClient CreateReasoningChatClient(
    AiOptions options,
    ILoggerFactory loggerFactory,
    Func<IChatClient, IChatClient>? innerWrapper = null)
```

Standart client'a ek:
- `ReasoningModel` yoksa `Model`'a düşer
- `ReasoningEffort` parametresi `ChatOptions.AdditionalProperties`'e konur
- `innerWrapper` ile telemetry decorator inject edilebilir

### `innerWrapper` parametresi

Composition Root telemetri decorator inject etmek için:

```csharp
var reasoningClient = AiClientFactory.CreateReasoningChatClient(
    options,
    loggerFactory,
    innerWrapper: rawClient => new TelemetryChatClient(rawClient, ...));
```

`rawClient` parametresi factory'nin az önce yarattığı provider client'ı; wrapper TelemetryChatClient ile sarmalar. Sonuç ReasoningChatClient içinde tutulur.

### Require() helper

```csharp
static string Require(string? value, string keyName)
{
    if (string.IsNullOrWhiteSpace(value))
        throw new InvalidOperationException($"AI configuration eksik: {keyName}");
    return value;
}
```

Eksik config tespiti — `Require(options.OpenAI.ApiKey, "AI:OpenAI:ApiKey")` gibi kullanılır.

---

## GeneralChatClientAdapter

**Port:** `IGeneralChatClient`

Genel amaçlı LLM completion — reasoning değil, basit prompt → response.

### `CompleteAsync`

```csharp
public async Task<string> CompleteAsync(
    IEnumerable<ConversationMessage> messages,
    CancellationToken ct = default)
{
    var chatMessages = messages.Select(m => new ChatMessage(RoleFor(m.Role), m.Text)).ToList();
    try
    {
        var response = await _client.GetResponseAsync(chatMessages, options: null, ct);
        return response.Text ?? string.Empty;
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
    catch (Exception ex)
    {
        throw ExceptionTranslator.Translate(ex, "General chat completion failed");
    }
}
```

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

OpenAI o-series modelleri için — derin düşünme (chain-of-thought) yapan modeller.

### Property'ler

```csharp
public string ModelName { get; }
public string ReasoningEffort { get; }   // "low", "medium", "high"
```

Constructor'da alınır, kullanıcıya gösterilebilir (admin paneli).

### `CompleteAsync` — non-streaming

```csharp
public async Task<string> CompleteAsync(IEnumerable<ConversationMessage> messages, CancellationToken ct)
{
    var chatMessages = messages.Select(...).ToList();
    var options = BuildOptions();
    var response = await _client.GetResponseAsync(chatMessages, options, ct);
    return response.Text ?? string.Empty;
}
```

### `StreamAsync` — streaming

```csharp
public async IAsyncEnumerable<string> StreamAsync(
    IEnumerable<ConversationMessage> messages,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    var chatMessages = messages.Select(...).ToList();
    var options = BuildOptions();

    await foreach (var update in _client.GetStreamingResponseAsync(chatMessages, options, ct))
    {
        if (!string.IsNullOrEmpty(update.Text))
            yield return update.Text;
    }
}
```

Boş/null text update'leri filtrelenir — sadece anlamlı text chunk'ları yield edilir.

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
- **Anthropic:** Sessizce yok sayar — "extended thinking" otomatik

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

Üç adapter de `ExceptionTranslator` kullanır:

```csharp
catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
catch (Exception ex)
{
    throw ExceptionTranslator.Translate(ex, "context message");
}
```

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

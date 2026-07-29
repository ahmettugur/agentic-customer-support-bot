# TelemetryChatClient

**Dosya:** `Chat/TelemetryChatClient.cs`  
**Base:** `Microsoft.Extensions.AI.DelegatingChatClient`  
**Pattern:** Decorator

Her `IChatClient` çağrısını sarmalar; span açar, token sayar, maliyet hesaplar, metric yayar.

---

## Neden decorator?

`Microsoft.Extensions.AI.IChatClient` standart arayüz; OpenAI, Anthropic, Ollama vb. her provider bu arayüzü implement eder. Decorator pattern ile:

```
Specialist Agent
   ↓ IChatClient çağrısı
TelemetryChatClient (decorator)        ← Span + metric + cost
   ↓ base.GetResponseAsync()
OpenAIChatClient / AnthropicChatClient ← Asıl LLM çağrısı
   ↓ HTTP request
Provider API
```

Decorator transparan — Specialist katmanın bu sınıfın varlığından haberi yok. Sadece `IChatClient` interface'ini bilir.

---

## Constructor

```csharp
public TelemetryChatClient(
    IChatClient inner,                       // Asıl client
    ICostCalculatorPort costCalculator,      // USD hesaplama
    CostUsageStore usageStore,               // In-memory aggregate
    string modelHint,                        // Default model adı
    string provider,                         // "openai", "anthropic", ...
    ILogger<TelemetryChatClient> logger,
    ILlmCallPersistencePort? persistence = null   // Opsiyonel DB kaydı
)
```

### modelHint vs actualModel

- **modelHint:** Constructor'da verilen — yapılandırmadan
- **actualModel:** Response'tan gelen — provider gerçekten hangi modeli kullandı

LLM provider'ları bazen istenen modelden farklısını döndürür (örn. fallback, A/B test). `ResolveModel(response)` actualModel'i tercih eder; yoksa hint'e döner.

---

## İki yol: non-streaming + streaming

### 1. `GetResponseAsync` (non-streaming)

```csharp
public override async Task<ChatResponse> GetResponseAsync(...)
{
    using var activity = StartLlmActivity("chat", _modelHint, _provider);
    var sw = Stopwatch.StartNew();
    try
    {
        response = await base.GetResponseAsync(...);
        sw.Stop();
        RecordSuccess(activity, response.Usage, sw.Elapsed.TotalMilliseconds, ResolveModel(response));
        return response;
    }
    catch (Exception ex)
    {
        sw.Stop();
        RecordFailure(activity, ex);
        throw;
    }
}
```

`response.Usage` (`UsageDetails`) direkt erişilebilir → tek noktada record.

### 2. `GetStreamingResponseAsync` (streaming)

Streaming'de `UsageDetails` her update'te değil **son update'te** gelir. `UsageContent` tipinde:

```csharp
await foreach (var update in stream)
{
    foreach (var c in update.Contents)
    {
        if (c is UsageContent uc) lastUsage = uc.Details;
    }
    if (!string.IsNullOrEmpty(update.ModelId)) lastModel = update.ModelId;
    yield return update;
}

sw.Stop();
RecordSuccess(activity, lastUsage, sw.Elapsed.TotalMilliseconds, lastModel ?? _modelHint);
```

- Her update yield edilir (caller'a anında akar)
- Stream bittikten **sonra** son `UsageDetails` ile `RecordSuccess` çağrılır
- Stream ortasında exception olursa span FAIL olarak işaretlenir

---

## `RecordSuccess` — ana iş

```csharp
private void RecordSuccess(Activity? activity, UsageDetails? usage, double durationMs, string? actualModel)
{
    var model = actualModel ?? _modelHint;
    long input  = usage?.InputTokenCount  ?? 0;
    long output = usage?.OutputTokenCount ?? 0;
    decimal cost = _costCalculator.CalculateCost(model, _provider, (int)input, (int)output);

    var modelTag    = new("ai.model", model);
    var providerTag = new("ai.provider", _provider);

    // 1) OpenTelemetry metric yayını
    LlmCallsCounter.Add(1, modelTag, providerTag);
    if (input > 0)  InputTokensCounter.Add(input, modelTag, providerTag);
    if (output > 0) OutputTokensCounter.Add(output, modelTag, providerTag);
    if (cost > 0)   CostUsdCounter.Add((double)cost, modelTag, providerTag);
    LlmLatencyHistogram.Record(durationMs, modelTag, providerTag);

    // 2) In-memory store (admin paneli için)
    _usageStore.Record(model, input, output, cost, durationMs);

    // 3) DB persistence (opsiyonel, fire-and-forget)
    PersistAsync(model, input, output, cost, durationMs);

    // 4) Span tag'leri + status
    activity?.SetTag("ai.model.actual", model);
    activity?.SetTag("ai.tokens.input", input);
    activity?.SetTag("ai.tokens.output", output);
    activity?.SetTag("ai.cost.usd", (double)cost);
    activity?.SetTag("ai.duration.ms", durationMs);
    activity?.SetStatus(ActivityStatusCode.Ok);
}
```

**Dört paralel yazım:**
1. OTLP'ye metric (Prometheus/Grafana)
2. In-memory store (admin paneli, `/api/telemetry/cost`)
3. PostgreSQL'e detay kayıt (opsiyonel)
4. Span'a tag (Jaeger trace detayı)

---

## Persistence (opsiyonel fire-and-forget)

```csharp
private void PersistAsync(string model, long input, long output, decimal cost, double durationMs)
{
    if (_persistence == null) return;
    var record = new LlmCallRecord(model, _provider, input, output, cost, durationMs, DateTime.UtcNow);
    _ = Task.Run(async () =>
    {
        try { await _persistence.RecordAsync(record); }
        catch { /* loglama ILlmCallPersistencePort kendi içinde yapar */ }
    });
}
```

`ILlmCallPersistencePort` (Postgres'te `PostgresLlmCallUsageSink`) DI'da varsa her LLM çağrısı **arka plan** task ile DB'ye yazılır:

- Fire-and-forget → ana request yolu beklemez (latency etkilenmez)
- Exception swallow → DB sorunu LLM çağrısını çökertmez
- Detay/audit için `analytics.llm_call_usages` tablosuna yazılır

`ILlmCallPersistencePort` opsiyonel bir bağımlılıktır (`sp.GetService<...>()` ile nullable alınır) — kayıtlı değilse `_persistence` null olur ve sadece OTLP + in-memory `CostUsageStore` çalışır. Pratikte `ILlmCallPersistencePort` her zaman `PostgresLlmCallUsageSink` olarak kayıtlıdır (bkz. [Persistence — Postgres vs InMemory](../adapters-persistence/README.md#postgres-vs-inmemory)); "InMemory provider" diye ayrı bir çalışma modu yoktur.

---

## Failure handling

```csharp
private void RecordFailure(Activity? activity, Exception ex)
{
    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
    activity?.AddTag("error.type", ex.GetType().FullName);
    _logger.LogWarning(ex, "LLM call failed (model={Model})", _modelHint);
    // Re-throw caller tarafında — exception yutulmaz
}
```

Exception:
- Span'a `Error` status + error.type tag eklenir
- Warning loglanır
- **Yeniden fırlatılır** (`throw` ile caller'a)

Bu sayede Specialist agent LLM hatasını görür, fallback davranışını uygulayabilir (örn. `WellKnown.FallbackMessages.ReasoningUnavailable`).

---

## Performans etkisi

| İşlem | Maliyet |
|---|---|
| Activity start/stop | ~1 µs |
| Counter.Add | ~100 ns |
| Histogram.Record | ~200 ns |
| CostCalculator | ~1 µs (dict lookup) |
| CostUsageStore.Record | ~5 µs (lock + update) |
| Persistence (async) | 0 (fire-and-forget) |

**Toplam overhead:** LLM çağrısının yanında (~500 ms-5 s) ihmal edilebilir.

---

## Test edilebilirlik

`DelegatingChatClient`'ı kullanması test'i kolaylaştırır:

```csharp
var fakeClient = new FakeChatClient(returnUsage: new UsageDetails { InputTokenCount = 100, OutputTokenCount = 50 });
var telemetry = new TelemetryChatClient(fakeClient, fakeCostCalc, store, "test-model", "test", logger);

await telemetry.GetResponseAsync(messages);

Assert.Equal(100, store.GetSnapshot().TotalInputTokens);
Assert.Equal(50, store.GetSnapshot().TotalOutputTokens);
```

Span'leri de OpenTelemetry test SDK ile yakalayabilirsin.

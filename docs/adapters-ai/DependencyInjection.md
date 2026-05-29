# Dependency Injection

**Dosya:** `DependencyInjection/AiAdapterServiceCollectionExtensions.cs`

---

## `AddAiAdapters`

```csharp
public static IServiceCollection AddAiAdapters(
    this IServiceCollection services,
    IConfiguration configuration)
```

AI altyapısının **bir kısmını** kaydeder. `IChatClient` kaydı bu extension'da **yapılmaz** — Composition Root (Api) sorumluluğunda (telemetry wrapping nedeniyle).

---

## Kayıt sırası

```
1. Options binding
   - AiOptions (configuration["AI"])
   - SemanticMemoryOptions (configuration["SemanticMemory"])
   - SelfImprovementOptions (configuration["SelfImprovement"])

2. Realtime
   - RealtimeFunctionTools (Singleton — tool şema kataloğu)
   - IRealtimeVoiceTransport → OpenAiRealtimeClientAdapter (Scoped)

3. Semantic Memory (sadece SemanticMemoryOptions.Enabled true ise)
   - IEmbeddingPort → OpenAiEmbeddingAdapter (Singleton)
   - IVectorMemoryPort → QdrantVectorMemoryAdapter (Singleton)
```

### Neden Realtime Scoped, diğerleri Singleton?

| Port | Yaşam döngüsü | Neden |
|---|---|---|
| `IEmbeddingPort` | Singleton | Tek HTTP client paylaşılır, stateless |
| `IVectorMemoryPort` | Singleton | Tek Qdrant gRPC client, stateless |
| `IRealtimeVoiceTransport` | **Scoped** | Her WebSocket bağlantısı kendi state'i (browser→OpenAI) |

Realtime adapter `ClientWebSocket` tutar — bu instance başka kullanıcının ses akışına karışmamalı.

---

## Semantic Memory koşullu kayıt

```csharp
var memoryOptions = configuration.GetSection("SemanticMemory").Get<SemanticMemoryOptions>();
if (memoryOptions?.Enabled == true)
{
    services.AddSingleton<IEmbeddingPort, OpenAiEmbeddingAdapter>();
    services.AddSingleton<IVectorMemoryPort, QdrantVectorMemoryAdapter>();
}
```

`SemanticMemory:Enabled = false` ise embedding ve vector store kaydedilmez. Application katmanı `IMemoryPort.Enabled` üzerinden bu durumu kontrol eder ve `DisabledMemoryPort` (no-op) kullanır.

Bu sayede:
- Qdrant kurulumu olmadan uygulama çalışır
- OpenAI embedding maliyeti opsiyonel

---

## Composition Root pattern — neden IChatClient burada değil?

`IChatClient` kayıt **`Api/Program.cs`'de** yapılır:

```csharp
// Api/Program.cs
services.AddSingleton<IChatClient>(sp =>
{
    var aiOptions = sp.GetRequiredService<IOptions<AiOptions>>().Value;

    // 1) Asıl client (provider-spesifik)
    IChatClient inner = AiClientFactory.CreateStandardChatClient(aiOptions);

    // 2) Telemetry decorator
    var costCalc = sp.GetRequiredService<ICostCalculatorPort>();
    var usageStore = sp.GetRequiredService<CostUsageStore>();
    var persistence = sp.GetService<ILlmCallPersistencePort>();

    return new TelemetryChatClient(
        inner,
        costCalc,
        usageStore,
        modelHint: ResolveModelHint(aiOptions),
        provider: aiOptions.Provider.ToString().ToLowerInvariant(),
        sp.GetRequiredService<ILogger<TelemetryChatClient>>(),
        persistence);
});

// Reasoning client için (decorate parametresi ile telemetri)
services.AddSingleton<IReasoningChatClient>(sp =>
{
    var aiOptions = sp.GetRequiredService<IOptions<AiOptions>>().Value;
    return AiClientFactory.CreateReasoningChatClient(
        aiOptions,
        decorate: inner => new TelemetryChatClient(inner, ...));
});

services.AddSingleton<IGeneralChatClient>(sp =>
    new GeneralChatClientAdapter(sp.GetRequiredService<IChatClient>()));
```

### Neden?

`Adapters.AI` projesi `Adapters.Telemetry`'ye bağımlı **değil** (ve olmamalı). İki adapter birbirinden habersiz; **Composition Root** ikisini birleştirir.

Bu pattern Dependency Inversion'a uygun: Hiçbir adapter "ben telemetri ile sarıl" demek zorunda değil — composition root karar verir.

---

## Options sectionları

| Config key | Sınıf | İçeriği |
|---|---|---|
| `AI` | `AiOptions` | Provider seçimi + 3 sağlayıcı + Realtime |
| `SemanticMemory` | `SemanticMemoryOptions` | Embedding model, dimension, Qdrant host |
| `SelfImprovement` | `SelfImprovementOptions` | LessonMiner config (threshold, take, vb.) |

Detaylar için [Options.md](Options.md).

---

## Kullanım

`Program.cs`:

```csharp
services.AddAiAdapters(configuration);

// IChatClient + telemetry wrap (Composition Root sorumluluğu):
services.AddSingleton<IChatClient>(sp => /* ... */);
services.AddSingleton<IReasoningChatClient>(sp => /* ... */);
services.AddSingleton<IGeneralChatClient>(sp => /* ... */);
```

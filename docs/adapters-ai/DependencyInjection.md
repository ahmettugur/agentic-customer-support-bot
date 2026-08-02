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

`IChatClient` kayıt **`CustomerSupportBot.Api/Extensions/AiServicesExtensions.cs`** (`AddAiServices`) içinde yapılır — `Program.cs` sadece bu extension metodunu çağırır:

```csharp
// Api/Extensions/AiServicesExtensions.cs
public static IServiceCollection AddAiServices(this IServiceCollection services, IConfiguration configuration)
{
    services.AddAiAdapters(configuration);

    services.AddSingleton<IChatClient>(sp =>
    {
        var options = sp.GetRequiredService<IOptions<AiOptions>>().Value;
        var inner = AiClientFactory.CreateStandardChatClient(options);
        return WrapWithTelemetry(sp, inner, ResolveStandardModel(options), options.Provider.ToString());
    });

    services.AddSingleton<ReasoningChatClient>(sp =>
    {
        var options = sp.GetRequiredService<IOptions<AiOptions>>().Value;
        return AiClientFactory.CreateReasoningChatClient(options, inner =>
            WrapWithTelemetry(sp, inner, ResolveReasoningModel(options), options.Provider.ToString()));
    });
    services.AddSingleton<IReasoningChatClient>(sp =>
        sp.GetRequiredService<ReasoningChatClient>());   // önce concrete olarak kaydedilir, sonra mapper

    services.AddSingleton<IGeneralChatClient>(sp =>
        new GeneralChatClientAdapter(sp.GetRequiredService<IChatClient>()));

    return services;
}

private static IChatClient WrapWithTelemetry(IServiceProvider sp, IChatClient inner, string modelHint, string provider)
{
    var telemetryOptions = sp.GetRequiredService<IOptions<TelemetryOptions>>().Value;
    if (!telemetryOptions.Enabled) return inner;   // Telemetry kapalıysa ham client döner, sarmalama yapılmaz

    return new TelemetryChatClient(inner, /* ... */);
}
```

`ResolveStandardModel`/`ResolveReasoningModel` iki ayrı metottur (tek bir `ResolveModelHint` helper'ı yoktur) — her ikisi de `AiOptions.Provider`'a göre OpenAI/AzureOpenAI model adını seçer.

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

`Program.cs` yalnızca çağırır:

```csharp
builder.Services.AddAiServices(builder.Configuration);
```

`AddAiServices` (`AiServicesExtensions.cs`) içeride `AddAiAdapters` (Adapters.AI) ile `IChatClient`/`IReasoningChatClient`/`IGeneralChatClient` kayıtlarını + telemetri sarmalamasını birleştirir.

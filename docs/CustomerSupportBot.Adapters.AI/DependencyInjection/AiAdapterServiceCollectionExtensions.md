# AiAdapterServiceCollectionExtensions

- **Kaynak:** `CustomerSupportBot.Adapters.AI/DependencyInjection/AiAdapterServiceCollectionExtensions.cs`
- **Tür:** `public static class`
- **Namespace:** `CustomerSupportBot.Adapters.AI.DependencyInjection`

## Ne işe yarar?

`AiAdapterServiceCollectionExtensions`, `CustomerSupportBot.Adapters.AI` katmanında bulunan strongly-typed seçenekleri (`AiOptions`, `SemanticMemoryOptions`, `SelfImprovementOptions`), Realtime ses araçlarını (`RealtimeFunctionTools`), WebSocket ses taşıyıcısını (`OpenAiRealtimeClientAdapter` -> `IRealtimeVoiceTransport`) ve anlamsal bellek adaptörlerini (`OpenAiEmbeddingAdapter` -> `IEmbeddingPort`, `QdrantVectorMemoryAdapter` -> `IVectorMemoryPort`) `IServiceCollection` IoC konteynerine kaydeden DI uzantısıdır.

## Hangi amaçla kullanılır`?

Composition Root (`CustomerSupportBot.Api`) tarafında tek satırla (`services.AddAiAdapters(configuration)`) tüm AI altyapı servislerini güvenli şekilde ayağa kaldırmak için kullanılır.

## Sorumlulukları

- **Üstlendiği:**
  - `AiOptions`, `SemanticMemoryOptions` ve `SelfImprovementOptions` konfigürasyon bölümlerini bağlamak.
  - `RealtimeFunctionTools` sınıfını Singleton olarak kaydetmek.
  - `IRealtimeVoiceTransport` uygulayıcısı olarak `OpenAiRealtimeClientAdapter`'ı her WebSocket bağlantısına özel `Scoped` olarak kaydetmek.
  - `SemanticMemory.Enabled` açık ise `IEmbeddingPort` ve `IVectorMemoryPort` adaptörlerini Singleton olarak kaydetmek.

## Metotlar ve İç Çalışma Mantıkları

### 1. `AddAiAdapters`
```csharp
public static IServiceCollection AddAiAdapters(
    this IServiceCollection services,
    IConfiguration configuration)
```
- **Ne işe yarar?:** AI adaptörü servislerini ve port implementasyonlarını IoC konteynerine ekler.
- **İç Mantığı:**
  1. `services.Configure<AiOptions>(...)` yapılandırmalarını bağlar.
  2. Realtime bağımlılıklarını kaydeder (`RealtimeFunctionTools` Singleton, `OpenAiRealtimeClientAdapter` Scoped).
  3. `SemanticMemoryOptions.Enabled` aktif ise `OpenAiEmbeddingAdapter` ve `QdrantVectorMemoryAdapter` singleton kayıtlarını yapar.

## Bağımlılıklar

- [IEmbeddingPort](../OpenAi/OpenAiEmbeddingAdapter.md)
- [IVectorMemoryPort](../Qdrant/QdrantVectorMemoryAdapter.md)
- [IRealtimeVoiceTransport](../Realtime/OpenAiRealtimeClientAdapter.md)
- [RealtimeFunctionTools](../Realtime/RealtimeFunctionTools.md)
- [AiOptions](../Options/AiProviderOptions.md)

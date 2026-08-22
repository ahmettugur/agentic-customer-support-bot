# CustomerSupportBot.Adapters.AI

Bu klasör, hexagonal mimaride **Driven Adapter (Çıkış Adaptörü)** rolünü üstlenen; yapay zeka (LLM), akıl yürütme (Reasoning), metin vektörleştirme (Embedding), gerçek zamanlı ses iletişimi (Realtime WebSocket) ve vektör veritabanı (Qdrant) entegrasyonlarını Microsoft Extensions AI (MEAI) standartlarıyla somutlaştıran adaptördür.

## Dizin Yapısı

- [Chat/](Chat/README.md) — Standart sohbet, özetleme ve o-serisi muhakeme istemcileri:
  - [AiClientFactory](Chat/AiClientFactory.md) — Sağlayıcı türüne (`OpenAI`, `AzureOpenAI`) göre `IChatClient` ve `ReasoningChatClient` inşa eden fabrika.
  - [GeneralChatClientAdapter](Chat/GeneralChatClientAdapter.md) — [IGeneralChatClient](../CustomerSupportBot.Application/Ports/Outbound/AI/IGeneralChatClient.md) uygulayıcısı; bağlam özetleme ve genel metin üretim adaptörü.
  - [ReasoningChatClient](Chat/ReasoningChatClient.md) — [IReasoningChatClient](../CustomerSupportBot.Application/Ports/Outbound/AI/IReasoningChatClient.md) uygulayıcısı; `reasoning_effort` parametresiyle derin muhakeme yürüten adaptör.
- [Realtime/](Realtime/README.md) — OpenAI Realtime API (WebSocket / Ses) entegrasyonu:
  - [OpenAiRealtimeClientAdapter](Realtime/OpenAiRealtimeClientAdapter.md) — [IRealtimeClientPort](../CustomerSupportBot.Application/Ports/Outbound/AI/IRealtimeVoiceTransport.md) uygulayıcısı; çift yönlü ses/metin akışı ve araç çağırma köprüsü.
  - [RealtimeFunctionTools](Realtime/RealtimeFunctionTools.md) — Realtime ses oturumu için tanımlanan araç şemaları.
- [Qdrant/](Qdrant/QdrantVectorMemoryAdapter.md) — [IVectorMemoryPort](../CustomerSupportBot.Application/Ports/Outbound/AI/IVectorMemoryPort.md) uygulayıcısı; Qdrant gRPC istemcisiyle koleksiyon yönetimi, payload filtreleme ve cosine similarity vektör araması.
- [OpenAi/](OpenAi/OpenAiEmbeddingAdapter.md) — [IEmbeddingPort](../CustomerSupportBot.Application/Ports/Outbound/AI/IEmbeddingPort.md) uygulayıcısı; metinleri vektörleştiren adaptör.
- [Options/](Options/AiProviderOptions.md) — `AiProvider`, `AiOptions`, `RealtimeOptions`, `OpenAiOptions`, `AzureOpenAiOptions` yapılandırma modelleri.
- [DependencyInjection/](DependencyInjection/AiAdapterServiceCollectionExtensions.md) — `AddAiAdapters` DI kayıt uzantısı.
- [ExceptionTranslator](ExceptionTranslator.md) — OpenAI, Azure ve Qdrant altyapı istisnalarını DomainException'a çevirici.

## Mimari Rolü ve Yetenekleri

- **Microsoft Extensions AI (MEAI) Uyumluluğu:** Tüm LLM istemcileri `Microsoft.Extensions.AI.IChatClient` ve `IEmbeddingGenerator` standartlarını temel alır.
- **Çoklu Sağlayıcı Desteği:** `appsettings.json` üzerinden tek satırla OpenAI ve Azure OpenAI arasında geçiş yapabilme.
- **Realtime Ses Protokolü:** WebSocket üzerinden OpenAI Realtime API ile düşük gecikmeli (low-latency) çift yönlü PCM16 ses akışı ve ses üzerinden anlık araç çalıştırma.
- **Qdrant Vektör Belleği:** Şirket bilgi bankası (`cs_knowledge`) ve kullanıcı episodik hafızası (`cs_episodes`) için gRPC tabanlı yüksek performanslı anlamsal arama.

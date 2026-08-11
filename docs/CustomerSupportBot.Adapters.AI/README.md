# CustomerSupportBot.Adapters.AI

LLM, embedding, vector store ve gerçek zamanlı ses altyapısı için adapter katmanı.

> 💡 **Analiz notu:** Bu katman projenin "konuşma yeteneği"dir. Application katmanı "bir LLM'e sor" der ama hangi LLM, hangi API, hangi SDK — hepsini bu adapter bilir. Application hiçbir zaman `OpenAIClient` görmez, sadece `IReasoningChatClient.CompleteAsync()` çağırır. Bu sayede yarın OpenAI yerine Anthropic kullansak, sadece bu klasör değişir.

İki sağlayıcı desteklenir:

- **OpenAI** (GPT-4o, GPT-4-turbo, o1-mini reasoning, vb.)
- **Azure OpenAI** (aynı modeller, Azure deployment'lar)

Ek hizmetler:

- **OpenAI Embedding API** — text-embedding-3-small/large
- **Qdrant** vector store — semantic memory
- **OpenAI Realtime API** — voice I/O (WebSocket)

---

## Klasör yapısı

```
CustomerSupportBot.Adapters.AI/
├── Chat/
│   ├── AiClientFactory.cs              ← Provider switch (OpenAI/Azure)
│   ├── GeneralChatClientAdapter.cs     ← IGeneralChatClient
│   └── ReasoningChatClient.cs          ← IReasoningChatClient (o-series reasoning)
├── OpenAi/
│   └── OpenAiEmbeddingAdapter.cs       ← IEmbeddingPort
├── Qdrant/
│   └── QdrantVectorMemoryAdapter.cs    ← IVectorMemoryPort
├── Realtime/
│   ├── OpenAiRealtimeClientAdapter.cs  ← IRealtimeVoiceTransport
│   └── RealtimeFunctionTools.cs        ← Read-only tool registry
├── DependencyInjection/
│   └── AiAdapterServiceCollectionExtensions.cs
├── Options/
│   └── AiProviderOptions.cs
└── ExceptionTranslator.cs
```

---

## Dokümantasyon haritası

| Doküman | Kapsam |
| --- | --- |
| [DependencyInjection.md](DependencyInjection.md) | `AddAiAdapters`, options binding, Composition Root pattern |
| [Options.md](Options.md) | AiProviderOptions yapılandırma şeması |
| [ChatClients.md](ChatClients.md) | AiClientFactory + GeneralChatClientAdapter + ReasoningChatClient |
| [Embedding.md](Embedding.md) | OpenAiEmbeddingAdapter (batch + provider fallback) |
| [VectorMemory.md](VectorMemory.md) | QdrantVectorMemoryAdapter (payload şeması, search, filter) |
| [Realtime.md](Realtime.md) | OpenAiRealtimeClientAdapter + RealtimeFunctionTools |
| [ExceptionTranslator.md](ExceptionTranslator.md) | HTTP + gRPC exception mapping |

---

## Port → Adapter eşlemesi

| Port (Application) | Adapter | Singleton/Scoped |
| --- | --- | --- |
| `IGeneralChatClient` | `GeneralChatClientAdapter` | Singleton |
| `IReasoningChatClient` | `ReasoningChatClient` | Singleton |
| `IEmbeddingPort` | `OpenAiEmbeddingAdapter` | Singleton |
| `IVectorMemoryPort` | `QdrantVectorMemoryAdapter` | Singleton |
| `IRealtimeVoiceTransport` | `OpenAiRealtimeClientAdapter` | **Scoped** (per WebSocket) |

`IRealtimeVoiceTransport` Scoped çünkü her browser WebSocket bağlantısı kendi OpenAI WebSocket'ine sahip — Singleton paylaşım imkansız.

---

## Provider seçimi

Tek `AI:Provider` config değeri uygulamanın hangi sağlayıcıya gideceğini belirler:

```json
{
  "AI": {
    "Provider": "OpenAI",   // veya "AzureOpenAI"
    "OpenAI": {
      "ApiKey": "sk-...",
      "Model": "gpt-4o-mini",
      "ReasoningModel": "o1-mini",
      "ReasoningEffort": "medium"
    },
    "AzureOpenAI": {
      "Endpoint": "https://...openai.azure.com",
      "ApiKey": "...",
      "Deployment": "gpt-4o-mini-prod",
      "ReasoningDeployment": "o1-mini-prod"
    },
    "Realtime": {
      "Enabled": true,
      "Model": "gpt-realtime-2",
      "Voice": "alloy",
      "VadSilenceMs": 600,
      "ReasoningEffort": "low",
      "TranscriptionModel": "gpt-4o-transcribe",
      "TranscriptionLanguage": "tr"
    }
  }
}
```

---

## Composition Root pattern

`AddAiAdapters` **`IChatClient`'ı kayıt etmez** — bu sorumluluk `Api` katmanına bırakılmıştır.

Neden? `IChatClient` `Adapters.Telemetry/TelemetryChatClient` ile sarmalanmalı. Bu compose adımı:

- `Adapters.AI` → asıl client'ı sağlar (OpenAI/Azure)
- `Adapters.Telemetry` → decorator (TelemetryChatClient)
- `Api` (Composition Root) → ikisini birleştirir

`AddAiAdapters` sadece embedding + vector + realtime + options kaydeder. Chat client kaydı için `CustomerSupportBot.Api/Extensions/AiServicesExtensions.cs` (`AddAiServices`) bak.

---

## Yapay zekâ akışları

### Chat (text)

```
PortService → IGeneralChatClient.CompleteAsync()
              IReasoningChatClient.CompleteAsync() / StreamAsync()
   ↓
GeneralChatClientAdapter / ReasoningChatClient
   ↓
TelemetryChatClient (decorator)
   ↓
Microsoft.Extensions.AI.IChatClient
   ↓
Provider SDK (OpenAI / Azure)
   ↓
HTTPS API
```

### Semantic Memory

```
LessonMiner / CustomerProfileService → IVectorMemoryPort.SearchAsync()
   ↓
QdrantVectorMemoryAdapter
   ├─ IEmbeddingPort.EmbedAsync(query)     → OpenAI embedding API
   └─ Qdrant.SearchAsync(vector, topK)     → gRPC
```

### Realtime (voice)

```
Browser WebSocket (PCM 24kHz)
   ↓
Api/Realtime endpoint
   ↓
RealtimeBridge / RealtimeNative (Application)
   ↓
OpenAiRealtimeClientAdapter (Adapters.AI)
   ↓
OpenAI Realtime WebSocket
```

---

## Bağımlılıklar

| Paket | Amaç |
| --- | --- |
| `Microsoft.Extensions.AI` | `IChatClient` interface |
| `Microsoft.Agents.AI.OpenAI` | OpenAI istemcisi (OpenAI SDK'yı transitive getirir — doğrudan `OpenAI` paket referansı yoktur) |
| `Azure.AI.OpenAI` | Azure OpenAI SDK |
| `Qdrant.Client` | Qdrant gRPC client |
| `System.Net.WebSockets` | Realtime WS bağlantısı |

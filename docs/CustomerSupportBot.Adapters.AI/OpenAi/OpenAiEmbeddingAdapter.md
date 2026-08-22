# OpenAiEmbeddingAdapter

- **Kaynak:** `CustomerSupportBot.Adapters.AI/OpenAi/OpenAiEmbeddingAdapter.cs`
- **Tür:** `public sealed class : IEmbeddingPort`
- **Namespace:** `CustomerSupportBot.Adapters.AI.OpenAi`

## Ne işe yarar?

`OpenAiEmbeddingAdapter`, Application katmanındaki [IEmbeddingPort](../../CustomerSupportBot.Application/Ports/Outbound/AI/IEmbeddingPort.md) portunu uygulayan; OpenAI veya Azure OpenAI `text-embedding-3-small` / `text-embedding-3-large` modelleri üzerinden metinleri sayısal vektör dizilerine (`float[]`) dönüştüren adaptördür.

## Hangi amaçla kullanılır`?

- RAG bilgi bankası dokümanlarını ve kullanıcı sorularını vektörleştirmek.
- **Sağlayıcı Fallback Mekanizması:** Önce aktif sağlayıcıya (`AiOptions.Provider`) bakar; API anahtarı yoksa diğer sağlayıcıya (Azure ➔ OpenAI veya OpenAI ➔ Azure) otomatik geri çekilir (fallback).
- **Toplu Vektörleştirme (Batching):** `EmbedBatchAsync` çağrılarında büyük metin listelerini 64'lük paketlere bölerek (chunking) OpenAI API istek sınırlarına takılmadan yüksek performansla vektörleştirmek.

## Sorumlulukları

- **Üstlendiği:**
  - `EmbedAsync` ile tekil metin vektörü üretmek.
  - `EmbedBatchAsync` ile toplu metin vektörleri üretmek.
  - Vektör boyutunu (`Dimension`) sunmak.

## Constructor ve Başlatma Mantığı

```csharp
public OpenAiEmbeddingAdapter(
    IOptions<AiOptions> aiOptions,
    IOptions<SemanticMemoryOptions> memoryOptions,
    ILogger<OpenAiEmbeddingAdapter> logger)
```

### Constructor İçerisinde Yapılan İşler:
1. `Dimension = memoryOptions.Value.Embedding.Dimension` atanır.
2. Sağlayıcı tercihi (`ai.Provider`) kontrol edilir:
   - `AzureOpenAI` seçiliyse ve endpoint/key varsa `AzureOpenAIClient.GetEmbeddingClient(emb.Model)` kurulur.
   - `OpenAI` seçiliyse ve key varsa `OpenAIClient.GetEmbeddingClient(emb.Model)` kurulur.
   - Tanımlı değilse diğer sağlayıcı anahtarlarına fallback denenir.
   - Hiçbir anahtar bulunamazsa `_client = null` bırakılır ve uyarı loglanır.

## Metotlar ve İç Çalışma Mantıkları

### 1. `EmbedAsync`
```csharp
public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
```
- **Ne işe yarar?:** Tek bir metni vektöre dönüştürür.
- **İç Mantığı:** `_client` null ise hata atar. Metin boşsa sıfırlardan oluşan `float[Dimension]` döner. Dolu ise `_client.GenerateEmbeddingAsync` çağrılır ve `result.Value.ToFloats().ToArray()` döner.

### 2. `EmbedBatchAsync`
```csharp
public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
    IReadOnlyList<string> texts,
    CancellationToken ct = default)
```
- **Ne işe yarar?:** Çoklu metin listesini toplu olarak vektörleştirir.
- **İç Mantığı:** `texts` listesi 64'lük dilimlere (`batchSize = 64`) bölünür. Her dilim için `GenerateEmbeddingsAsync` çalıştırılır ve sonuçlar birleştirilerek döndürülür.

## Özellikler (Properties)

| Özellik | Tür | Açıklama |
|---|---|---|
| `Dimension` | `int` | Üretilen vektörün boyut sayısı (ör. 1536). |
| `IsConfigured` | `bool` | Embedding istemcisinin başarıyla yapılandırılıp yapılandırılmadığı (`_client is not null`). |

## Bağımlılıklar

- [IEmbeddingPort](../../CustomerSupportBot.Application/Ports/Outbound/AI/IEmbeddingPort.md)
- `OpenAI.Embeddings.EmbeddingClient`
- `Azure.AI.OpenAI.AzureOpenAIClient`
- [AiOptions](../Options/AiProviderOptions.md)

# IEmbeddingPort

**Kaynak:** `Ports/Outbound/AI/IEmbeddingPort.cs`
**Implementasyon:** [`OpenAiEmbeddingAdapter`](../../../../CustomerSupportBot.Adapters.AI/OpenAi/OpenAiEmbeddingAdapter.md)

## 1. Ne İşe Yarar

Metni embedding vektörüne (`float[]`) çeviren secondary port. Semantic memory ve knowledge
base'in vektör aramasının temelini oluşturur.

## 2. Hangi Amaçla Kullanılır

`SemanticMemoryService` gibi Application servisleri, bir metni Qdrant'a yazmadan veya Qdrant'ta
arama yapmadan önce onu vektöre çevirmek için bu port'u çağırır.

## 3. Sorumlulukları

- **Üstlendiği:** Tekil (`EmbedAsync`) ve toplu (`EmbedBatchAsync`) embedding üretimi, vektör
  boyutunu (`Dimension`) ve sağlayıcının kullanılabilir olup olmadığını (`IsConfigured`)
  bildirmek.
- **Üstlenmediği:** Vektörün nereye/nasıl yazılacağı — o [`IVectorMemoryPort`](IVectorMemoryPort.md)'un işi.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `Adapters.AI/OpenAi/OpenAiEmbeddingAdapter` implemente eder (OpenAI embedding API'sini sarar).
- `SemanticMemoryOptions.EmbeddingOptions` (bkz. [SemanticMemoryOptions](SemanticMemoryOptions.md))
  hangi modelin/boyutun kullanılacağını konfigüre eder — `Dimension` property'si burayla
  senkron olmalıdır (uyuşmazlıkta bkz. `AllowDestructiveDimensionMigration` notu).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Hexagonal mimaride Application katmanı hangi embedding sağlayıcısının (OpenAI, yerel model vb.)
kullanıldığını bilmemelidir — yalnızca "metni vektöre çevir" sözleşmesini bilir. `IsConfigured`
özellikle önemlidir: API key yoksa bellek özelliği sessizce devre dışı kalmalı, uygulama
çökmemelidir.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Task<float[]> EmbedAsync(string text, CancellationToken ct = default)` | Tek metin için embedding üretir. |
| `Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)` | Toplu embedding — KB ingest gibi pahalı işlemler için tek istekte birden çok metin gönderir. |
| `int Dimension { get; }` | Vektör boyutu; Qdrant koleksiyonu yaratılırken kullanılır. |
| `bool IsConfigured { get; }` | Geçerli bir API key/endpoint var mı? `false` ise çağıran taraf belleği devre dışı kabul etmelidir. |

## 7. Bağımlılıklar

Port arayüzü bağımlılıksızdır. İmplementasyon bağımlılıkları için
`OpenAiEmbeddingAdapter.md`'ye bakın.

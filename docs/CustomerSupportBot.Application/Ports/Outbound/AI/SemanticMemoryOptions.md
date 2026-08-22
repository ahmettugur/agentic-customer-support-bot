# SemanticMemoryOptions

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/AI/SemanticMemoryOptions.cs`
- **Tür:** `public sealed class`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound.AI`

## Ne işe yarar?

`SemanticMemoryOptions`, Application/Ports/Driven/AI/SemanticMemoryOptions.cs appsettings.json > "SemanticMemory" bölümüne bind edilen opsiyonlar. VectorStore, Embedding, Retrieval ve Collections gibi domain-agnostik konfigürasyonları içerir. <summary> Embedding boyutu mevcut koleksiyonunkiyle uyuşmadığında koleksiyonun SİLİNİP yeniden oluşturulmasına izin verir. </summary>  <remarks>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`SemanticMemoryOptions`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Özellikler/Properties

- `Enabled` (`bool`): İlgili veriyi temsil eden özellik.
- `VectorStore` (`VectorStoreOptions`): İlgili veriyi temsil eden özellik.
- `Embedding` (`EmbeddingOptions`): İlgili veriyi temsil eden özellik.
- `Collections` (`CollectionOptions`): İlgili veriyi temsil eden özellik.
- `Retrieval` (`RetrievalOptions`): İlgili veriyi temsil eden özellik.
- `KnowledgeBase` (`KnowledgeBaseOptions`): İlgili veriyi temsil eden özellik.
- `Host` (`string`): İlgili veriyi temsil eden özellik.
- `Port` (`int`): İlgili veriyi temsil eden özellik.
- `UseHttps` (`bool`): İlgili veriyi temsil eden özellik.
- `ApiKey` (`string?`): İlgili veriyi temsil eden özellik.
- `AllowDestructiveDimensionMigration` (`bool`): İlgili veriyi temsil eden özellik.
- `Model` (`string`): İlgili veriyi temsil eden özellik.
- `Dimension` (`int`): İlgili veriyi temsil eden özellik.
- `Episodic` (`string`): İlgili veriyi temsil eden özellik.
- `Lessons` (`string`): İlgili veriyi temsil eden özellik.
- `Knowledge` (`string`): İlgili veriyi temsil eden özellik.
- `TopK` (`int`): İlgili veriyi temsil eden özellik.
- `MinScore` (`float`): İlgili veriyi temsil eden özellik.
- `MaxContextChars` (`int`): İlgili veriyi temsil eden özellik.
- `AutoIngestOnStartup` (`bool`): İlgili veriyi temsil eden özellik.
- `ChunkSize` (`int`): İlgili veriyi temsil eden özellik.
- `ChunkOverlap` (`int`): İlgili veriyi temsil eden özellik.
- `Enabled` (`bool`): İlgili veriyi temsil eden özellik.
- `MinRatingForLesson` (`int`): İlgili veriyi temsil eden özellik.
- `RecentTracesToScan` (`int`): İlgili veriyi temsil eden özellik.
- `RequireApprovalBeforeActivation` (`bool`): İlgili veriyi temsil eden özellik.

## Bağımlılıklar

- `CustomerSupportBot.Domain`

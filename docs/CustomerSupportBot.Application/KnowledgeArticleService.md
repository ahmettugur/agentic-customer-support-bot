# KnowledgeArticleService

## Ne İşe Yarar
Admin panelinden yönetilen bilgi tabanı makalelerinin use case servisidir. CRUD işlemlerini Postgres (kayıt otoritesi) ve Qdrant (türetilmiş indeks) arasında koordine eder.

## Hangi Amaçla Kullanılır
`MemoryEndpoints` → `IKnowledgeBasePort` arayüzü üzerinden çağrılır. Makale oluşturma, güncelleme, silme ve listeleme işlemlerini gerçekleştirir.

## Sorumlulukları
- Makale CRUD işlemlerini yönetmek.
- Makale kaydedildikten sonra içeriği chunk'lara ayırıp Qdrant'a indekslemek.
- İndeksleme başarısız olursa makaleyi yine de kaydetmek ama uyarı döndürmek.
- Silinen makalelerin chunk'larını Qdrant'tan temizlemek.
- Yayından kaldırılan makalelerin chunk'larını temizlemek.

## Diğer Katman ve Bileşenlerle İlişkileri
- **Implements**: `IKnowledgeBasePort` (Inbound port).
- **DI ile inject edilen**: `IKnowledgeArticleStore`, `ISemanticMemoryIngestor`, `SemanticMemoryOptions`.
- **İlişkili sınıf**: `KnowledgeBaseIngestionService` — aynı chunk algoritmasını paylaşır (`ChunkText` metodu).

## Kullanılma Nedeni ve Tasarım Yaklaşımı
DB (Postgres) kayıt otoritesidir; Qdrant türetilmiş indekstir. Sıra: önce DB'ye yaz, sonra indeksle. İndeksleme başarısız olsa bile makale kaybolmaz. Chunk sayısı DB'de tutulur — sonraki kayıtta doğru aralığın temizlenmesi için. İndeks yazılamadığında chunk sayısı eski değere geri alınır.

## Metotlar / Üyeler

| Metot | Açıklama |
|-------|----------|
| `ListAsync(ct)` | Tüm makaleleri listeler. |
| `GetAsync(id, ct)` | Tek makale getirir. |
| `CreateAsync(title, content, category, isPublished, updatedBy, ct)` | Yeni makale oluşturur ve indeksler. |
| `UpdateAsync(id, title, content, category, isPublished, updatedBy, ct)` | Mevcut makaleyi günceller ve yeniden indeksler. |
| `DeleteAsync(id, ct)` | Makaleyi siler; önce indeks chunk'larını temizler. |
| `BuildChunkDocuments(article, chunkSize, chunkOverlap)` | Makaleyi `MemoryDocument` chunk'larına çevirir (saf fonksiyon, test edilebilir). |

## Bağımlılıklar
- `IKnowledgeArticleStore` — Postgres CRUD.
- `ISemanticMemoryIngestor` — Qdrant write.
- `SemanticMemoryOptions` — Chunk size/overlap konfigürasyonu.

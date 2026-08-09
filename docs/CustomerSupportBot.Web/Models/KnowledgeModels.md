# KnowledgeModels

## Ne İşe Yarar
Bilgi tabanı (Knowledge Base) sayfasının DTO tanımlarını içerir. Makale listeleme, oluşturma, güncelleme ve kaydetme yanıtlarında kullanılır.

## Hangi Amaçla Kullanılır
[KnowledgeApiService](../Services/KnowledgeApiService.md) tarafından API istek/yanıt serialization'ında; `Knowledge.razor` sayfasında form binding ve listeleme işlemlerinde kullanılır.

## Sorumlulukları
Yalnızca veri taşıma — iş mantığı içermez. `KnowledgeArticleDto.Clone()` metodu UI'da düzenleme sırasında orijinal veriyi korumak için kopya oluşturur.

## Diğer Katman ve Bileşenlerle İlişkileri
- **Kullanan servis**: [KnowledgeApiService](../Services/KnowledgeApiService.md).
- **Kullanan sayfa**: `Pages/Knowledge.razor`.
- **Backend karşılığı**: `CustomerSupportBot.Api` → MemoryEndpoints'in döndürdüğü şema.

## Üyeler

| Sınıf | Açıklama |
|-------|----------|
| `KnowledgeArticleDto` | Makale okuma DTO'su: Id, Title, Content, Category, IsPublished, IndexedChunkCount, CreatedAt, UpdatedAt, UpdatedBy. `Clone()` metodu var. |
| `KnowledgeArticleInput` | Makale yazma input'u: Title, Content, Category, IsPublished. |
| `KnowledgeArticleSaveDto` | Kaydetme yanıtı: Article, Indexed, Warning. İndeksleme ayrı başarısız olabildiği için uyarı taşır. |

## Bağımlılıklar
Yok — saf DTO/class tanımları.

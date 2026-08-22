# KnowledgeArticleEntity

**Dosya:** `EfCore/Entities/Knowledge/KnowledgeArticleEntity.cs`
**Şema/Tablo:** `knowledge.articles`
**Configuration:** [KnowledgeArticleConfiguration](../../Configurations/Knowledge/KnowledgeArticleConfiguration.md)

## 1. Ne İşe Yarar

Admin panelinden yönetilen bir bilgi bankası (knowledge base) makalesini temsil eder —
ör. "iade politikası", "kargo süreleri" gibi bota beslenen kurumsal bilgi metinleri.

## 2. Hangi Amaçla Kullanılır

`KnowledgeBaseIngestor` (Api katmanı, arka plan işçisi) yayınlanmış (`IsPublished = true`)
makaleleri periyodik olarak Qdrant'a vektör embedding olarak indeksler
(`IndexedChunkCount` bu işlemin sonucudur); `SemanticMemoryContextProvider` bu indekslenmiş
içerikten sohbet sırasında ilgili parçaları arar.

## 3. Sorumlulukları

- **Üstlendiği:** Makale başlığı/içeriği/kategorisi, yayın durumu, kaç parçaya (chunk)
  bölünüp indekslendiği.
- **Üstlenmediği:** Embedding üretimi/vektör arama — bu Qdrant adaptörünün işi;
  `IndexedChunkCount` sadece "kaç parça üretildi" bilgisini tutar, parçaların kendisini değil.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`KnowledgeBaseIngestor` bu tabloyu okuyup Qdrant'a yazar; admin panelindeki makale
yönetim ekranı bu tabloyu CRUD'lar.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`IsPublished` ayrı bir alan olarak tutulur (makale silinmez, yayından kaldırılır) — böylece bir
makale geçici olarak devre dışı bırakılabilir ve tekrar yayına alınabilir, içerik/geçmiş
kaybolmaz. `IndexedChunkCount`, bir makalenin **hiç indekslenmemiş** (`0`) mi yoksa
**indekslenmiş ama içerik boş** mu olduğunu ayırt etmeyi sağlar — ingestor'ın bir sonraki
taramada bu makaleyi tekrar işlemesi gerekip gerekmediğine karar vermesinde kullanılabilir.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `Id` | `string` | Birincil anahtar. |
| `Title` | `string` | Makale başlığı. |
| `Content` | `string` | Makale içeriği (serbest metin). |
| `Category` | `string?` | Opsiyonel kategori etiketi. |
| `IsPublished` | `bool` | Yayında mı — sadece yayındaki makaleler indekslenir/kullanılır. |
| `IndexedChunkCount` | `int` | Qdrant'a kaç parça olarak indekslendiği. |
| `CreatedAt` | `DateTime` | Oluşturulma zamanı. |
| `UpdatedAt` | `DateTime` | Son güncelleme zamanı. |
| `UpdatedBy` | `string?` | Son güncelleyen admin. |

## 7. Bağımlılıklar

Yok — saf veri sınıfı.

## Bağlantılar

- [KnowledgeArticleConfiguration](../../Configurations/Knowledge/KnowledgeArticleConfiguration.md)
- [README](../README.md)

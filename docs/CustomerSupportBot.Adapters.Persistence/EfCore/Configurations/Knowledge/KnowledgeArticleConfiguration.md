# KnowledgeArticleConfiguration

**Dosya:** `EfCore/Configurations/Knowledge/KnowledgeArticleConfiguration.cs`
**Uyguladığı Arayüz:** `IEntityTypeConfiguration<KnowledgeArticleEntity>`
**Entity:** [KnowledgeArticleEntity](../../Entities/Knowledge/KnowledgeArticleEntity.md)

## 1. Ne İşe Yarar

`KnowledgeArticleEntity`'nin `knowledge.articles` tablosuna eşlemesini ve iki index'ini
tanımlar.

## 2. Hangi Amaçla Kullanılır

`CustomerSupportDbContext.OnModelCreating` tarafından otomatik uygulanır.

## 3. Sorumlulukları

Kolon eşlemeleri; `IsPublished` üzerinde arama index'i; `UpdatedAt` üzerinde azalan index.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

Bağımsız — başka bir Configuration'a FK ile bağlı değil.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`ix_articles_is_published` — `KnowledgeBaseIngestor`'ın "sadece yayındaki makaleleri tara"
sorgusunu hızlandırır (yayında olmayan makaleler taramanın büyük çoğunluğunu oluşturabilir).
`ix_articles_updated_at` (azalan) — admin panelinde "son güncellenen makaleler" listesi için.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Configure(EntityTypeBuilder<KnowledgeArticleEntity>)` | `Id` PK; `Title`/`Content`/`IsPublished`/`IndexedChunkCount`/`CreatedAt`/`UpdatedAt` zorunlu; `Category`/`UpdatedBy` opsiyonel; 2 index. |

## 7. Bağımlılıklar

Yok.

## Bağlantılar

- [KnowledgeArticleEntity](../../Entities/Knowledge/KnowledgeArticleEntity.md)
- [README](../README.md)

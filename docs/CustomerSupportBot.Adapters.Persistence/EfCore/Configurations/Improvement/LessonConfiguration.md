# LessonConfiguration

**Dosya:** `EfCore/Configurations/Improvement/LessonConfiguration.cs`
**Uyguladığı Arayüz:** `IEntityTypeConfiguration<LessonEntity>`
**Entity:** [LessonEntity](../../Entities/Improvement/LessonEntity.md)

## 1. Ne İşe Yarar

`LessonEntity`'nin `improvement.lessons` tablosuna eşlemesini ve iki index'ini tanımlar.

## 2. Hangi Amaçla Kullanılır

`CustomerSupportDbContext.OnModelCreating` tarafından otomatik uygulanır.

## 3. Sorumlulukları

Kolon eşlemeleri (`SourceTraceIdsJson` → `jsonb`); `Status` üzerinde arama index'i; `CreatedAt`
üzerinde azalan index.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

Bağımsız — `SourceTraceIdsJson` içindeki trace kimlikleri gerçek FK ile bağlı değildir.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`ix_lessons_status` — admin panelindeki "onay bekleyen dersler" listesi (`Status = Proposed`)
sık sorgulanır; `ix_lessons_created_at` (azalan) ise "en yeni dersler önce" sıralamasını
destekler.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Configure(EntityTypeBuilder<LessonEntity>)` | `Id` PK; `Title`/`LessonText`/`Observation`/`SourceTraceIdsJson`(`jsonb`)/`Status`/`CreatedAt` zorunlu; diğerleri opsiyonel; 2 index. |

## 7. Bağımlılıklar

Yok.

## Bağlantılar

- [LessonEntity](../../Entities/Improvement/LessonEntity.md)
- [README](../README.md)

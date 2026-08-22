# LessonEntity

**Dosya:** `EfCore/Entities/Improvement/LessonEntity.cs`
**Şema/Tablo:** `improvement.lessons`
**Configuration:** [LessonConfiguration](../../Configurations/Improvement/LessonConfiguration.md)

## 1. Ne İşe Yarar

Sistemin "self-improving loop" (kendini iyileştirme döngüsü) mekanizmasının ürettiği, admin
onayı bekleyen bir "öğrenilmiş ders" kaydını temsil eder — geçmiş konuşma trace'lerinden
çıkarılmış bir davranış önerisi.

## 2. Hangi Amaçla Kullanılır

Improvement servisi geçmiş `ReasoningTraceEntity` kayıtlarını analiz edip bir iyileştirme
önerisi (`LessonText`) üretir ve bu tabloya `"Proposed"` durumunda yazar; admin bunu onaylar/
reddeder; onaylanan dersler `VectorMemoryId` üzerinden Qdrant'a (vektör belleğe) yazılıp
gelecekteki konuşmalarda bağlam olarak kullanılabilir hale gelir.

## 3. Sorumlulukları

- **Üstlendiği:** Bir dersin metnini, hangi gözlemden çıkarıldığını, hangi trace'lere
  dayandığını ve onay durumunu taşımak.
- **Üstlenmediği:** Dersin LLM tarafından nasıl üretildiği (bu, Improvement servisinin işi),
  vektör embedding'in kendisi (bu Qdrant adaptörünün işi — `VectorMemoryId` sadece bir referans).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`SourceTraceIdsJson`, [ReasoningTraceEntity](../Observability/ReasoningTraceEntity.md)
kayıtlarının kimliklerini (JSON dizi) tutar — gerçek FK değil, mantıksal referans listesi.
`VectorMemoryId`, Qdrant'taki (Adapters.AI katmanı) karşılık gelen belgeye işaret eder.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`SourceTraceIdsJson` neden tek bir FK değil de bir JSON liste: bir ders genellikle **birden
fazla** geçmiş konuşmadan çıkarılan bir örüntüdür (ör. "müşteriler X konusunda hep aynı şekilde
yanlış anlaşılıyor") — tek bir trace'e bağlı olmak bu çok-kaynaklı doğayı ifade edemezdi.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `Id` | `string` | Birincil anahtar. |
| `Title` | `string` | Dersin kısa başlığı. |
| `LessonText` | `string` | Önerilen davranış/ders metni. |
| `Observation` | `string` | Dersin çıkarıldığı gözlem. |
| `SuggestedAgent` | `string?` | Hangi uzman ajana uygulanması önerildiği. |
| `SourceTraceIdsJson` | `string` | Kaynak trace kimlikleri, `jsonb` dizi. |
| `Status` | `string` | `"Proposed"` (varsayılan) \| onay/red sonrası durumlar. |
| `DecidedBy` | `string?` | Kararı veren admin. |
| `DecidedAt` | `DateTime?` | Karar zamanı. |
| `DecisionReason` | `string?` | Karar gerekçesi. |
| `CreatedAt` | `DateTime` | Oluşturulma zamanı. |
| `VectorMemoryId` | `string?` | Onaylandıysa Qdrant'taki karşılık gelen belge kimliği. |

## 7. Bağımlılıklar

Yok — saf veri sınıfı.

## Bağlantılar

- [LessonConfiguration](../../Configurations/Improvement/LessonConfiguration.md)
- [ReasoningTraceEntity](../Observability/ReasoningTraceEntity.md)
- [README](../README.md)

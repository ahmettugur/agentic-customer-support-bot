# ILessonStore

**Kaynak:** `Ports/Outbound/Persistence/ILessonStore.cs`
**İmplementasyonlar:** [`InMemoryLessonStore`](../../../../CustomerSupportBot.Adapters.Persistence/InMemory/InMemoryLessonStore.md), [`PostgresLessonStore`](../../../../CustomerSupportBot.Adapters.Persistence/Postgres/PostgresLessonStore.md)

## 1. Ne İşe Yarar

Self-Improving Loop'un (bkz. [SelfImprovementOptions](../AI/SelfImprovementOptions.md)) ürettiği
"ders" (`Lesson`) kayıtlarının kalıcılığı için secondary port.

## 2. Hangi Amaçla Kullanılır

`LessonMiner` düşük puanlı konuşmaları tarayıp aday dersler ürettiğinde `Add` ile kaydeder;
admin panelinde onay/red kararı `GetByStatus`/`Update` ile işlenir.

## 3. Sorumlulukları

- **Üstlendiği:** Ders kaydı CRUD'u ve durum bazlı sorgulama.
- **Üstlenmediği:** Dersin nasıl üretileceği (LLM analizi) — bu `LessonMiner`'ın işi; bu port
  yalnızca sonucu saklar.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`InMemoryLessonStore` (test) ve `PostgresLessonStore` (prod) implemente eder. Onaylanan dersler
[`IVectorMemoryPort`](../AI/IVectorMemoryPort.md) üzerinden `cs_lessons` koleksiyonuna
(bkz. [SemanticMemoryOptions](../AI/SemanticMemoryOptions.md)) indekslenir.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Dersler admin onayı olmadan aktive edilemez (`RequireApprovalBeforeActivation`) — bu port bu
onay durumunu (`LessonStatus`) taşır; LLM'in ürettiği bir "ders" doğrudan sisteme yayınlanmaz,
insan gözden geçirmesi zorunludur.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `void Add(Lesson lesson)` | Yeni ders adayı ekler. |
| `Lesson? Get(string id)` | Tekil sorgu. |
| `IReadOnlyList<Lesson> GetByStatus(LessonStatus status)` | Duruma göre filtreler (örn. `PendingApproval`). |
| `IReadOnlyList<Lesson> GetAll(int limit = 200)` | Tüm dersler. |
| `void Update(Lesson lesson)` | Ders durumunu/içeriğini günceller (örn. onay sonrası). |

## 7. Bağımlılıklar

Port arayüzü `CustomerSupportBot.Domain.Model.Improvement.Lesson`/`LessonStatus`'a bağımlıdır.

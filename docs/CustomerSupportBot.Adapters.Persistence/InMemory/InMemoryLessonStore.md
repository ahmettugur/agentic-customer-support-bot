# InMemoryLessonStore

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/InMemory/InMemoryLessonStore.cs`
- **Port:** `ILessonStore`
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.InMemory`

## 1. Ne İşe Yarar

Self-improving loop'un ürettiği, admin onayına sunulan "öğrenilmiş ders" (`Lesson`, Domain katmanı `Model/Improvement/Lesson.md`) kayıtlarını `ConcurrentDictionary<string, Lesson>` ile bellekte tutan en küçük ve en basit `InMemory` deposudur.

## 2. Hangi Amaçla Kullanıldığı

`PostgresLessonStore`'un tek-process karşılığı. Improvement döngüsünün ders önerilerini saklar.

## 3. Sorumlulukları

- `Add`/`Update` — ikisi de aslında aynı işlemi yapar (`_byId[lesson.Id] = lesson`), semantik olarak ayrılır ama implementasyonda fark yoktur.
- `Get` — tek kayıt.
- `GetByStatus(status)` — belirli durumdaki (ör. `PendingApproval`) dersleri, en yeniden eskiye sıralı döner.
- `GetAll(limit)` — tüm dersler, sınırlı sayıda.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `PostgresLessonStore` (`../Postgres/PostgresLessonStore.md`) ile aynı arayüzü uygular.
- `Lesson` modeli Domain katmanındadır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Hiçbir ek iş kuralı içermez — sadece CRUD; kapasite sınırlaması veya ring-buffer YOKTUR (diğer bazı `InMemory` sınıflarının aksine), çünkü ders sayısı doğası gereği düşük hacimli (insan onayı gerektiren, seyrek üretilen kayıtlar).

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Add(lesson)` | Kaydeder/üzerine yazar. |
| `Get(id)` | Tek kayıt, yoksa `null`. |
| `Update(lesson)` | `Add` ile aynı davranış. |
| `GetByStatus(status)` | Duruma göre filtrelenmiş, `CreatedAt` azalan sırada. |
| `GetAll(limit)` | En fazla `limit` kayıt (varsayılan 200), `CreatedAt` azalan sırada. |

## 7. Bağımlılıklar

- Yok.

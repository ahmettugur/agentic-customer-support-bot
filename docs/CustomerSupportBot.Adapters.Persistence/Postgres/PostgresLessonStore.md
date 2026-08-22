# PostgresLessonStore

**Dosya:** `Postgres/PostgresLessonStore.cs`
**Namespace:** `CustomerSupportBot.Adapters.Persistence.Postgres`
**Port:** [`ILessonStore`](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ILessonStore.md)

## 1. Ne İşe Yarar

Başarısız/düzeltilen turlardan çıkarılan self-improvement "ders"lerini (`Lesson` — `improvement.lessons` tablosu) hibrit cache + Redis pub/sub ile saklar.

## 2. Hangi Amaçla Kullanılır

Self-improvement döngüsü kötü bir turdan sonra bir `Lesson` taslağı üretip `Add` ile kaydeder; admin onayladıktan sonra `Update` ile `Status` değişir; onaylı dersler gelecekteki benzer sorgularda prompt'a beslenmek üzere `GetByStatus`/`GetAll` ile okunur.

## 3. Sorumlulukları

- Üstlendiği: ders CRUD'u, cross-pod cache senkronu.
- Üstlenmediği: dersin İÇERİĞİNİN üretimi (LLM tabanlı bir Improvement servisinin işi — bkz. `Services/Improvement`), dersin prompt'a nasıl enjekte edileceği.

## 4. İlişkiler

- `ILessonStore` portunu implemente eder.
- `IMessageBusPort` (Redis `csbot:lesson:upserted`), `IDbContextFactory<CustomerSupportDbContext>` enjekte edilir.

## 5. Tasarım Yaklaşımı

`Lesson` kaydı küçük/sınırlı olduğu için (approval kuyruğundaki delta yerine) her `Add`/`Update` sonrası TAM kayıt Redis'e yayınlanır — [`PostgresHumanAgentRegistry`](PostgresHumanAgentRegistry.md)/[`PostgresChatModeRegistry`](PostgresChatModeRegistry.md) ile aynı sadelik tercihi.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `void Add(Lesson lesson)` | Cache + DB INSERT + Redis yayını. |
| `Lesson? Get(string id)` | Cache'ten tek kayıt. |
| `void Update(Lesson lesson)` | Cache + DB UPDATE + Redis yayını (örn. admin onay/red durumu). |
| `IReadOnlyList<Lesson> GetByStatus(LessonStatus status)` | Cache'ten filtreli liste (örn. yalnızca onaylı dersler). |
| `IReadOnlyList<Lesson> GetAll(int limit = 200)` | Cache'ten tüm dersler, sınırlı. |

## 7. Bağımlılıklar

- `IDbContextFactory<CustomerSupportDbContext>`
- `IMessageBusPort`
- `ILogger<PostgresLessonStore>`

## Bağlantılar

- [ILessonStore](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ILessonStore.md)
- [Lesson](../../CustomerSupportBot.Domain/Model/Improvement/Lesson.md)

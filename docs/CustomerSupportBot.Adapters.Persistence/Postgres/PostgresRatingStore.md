# PostgresRatingStore

**Dosya:** `Postgres/PostgresRatingStore.cs`
**Namespace:** `CustomerSupportBot.Adapters.Persistence.Postgres`
**Port:** [`IRatingStore`](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IRatingStore.md)

## 1. Ne İşe Yarar

Müşteri memnuniyet anketlerini (1-5 yıldız + serbest metin yorum) `analytics`/`personalization` şemasındaki ilgili tabloya hibrit cache ile kaydeder.

## 2. Hangi Amaçla Kullanılır

Sohbet sonunda kullanıcıya sunulan "bu görüşmeyi puanla" ekranının backend'i (`Submit`); admin analitik panosu `GetAll`/`GetRecent`'i kullanır.

## 3. Sorumlulukları

- Üstlendiği: puan/yorum kaydı, oturum başına tekillik (bir oturum için birden fazla puan üretilirse davranışı — bkz. kaynak kodda `Submit`), cache senkronu.
- Üstlenmediği: puanların SLA/kalite metriklerine dönüştürülmesi (ayrı bir analitik/raporlama katmanının işi).

## 4. İlişkiler

- `IRatingStore` portunu implemente eder.
- `IMessageBusPort`, `IDbContextFactory<CustomerSupportDbContext>` enjekte edilir.

## 5. Tasarım Yaklaşımı

Diğer küçük-kayıt depoları (`PostgresLessonStore`, `PostgresHumanAgentRegistry`) ile aynı "hibrit cache + tam kayıt Redis yayını" deseni.

> 🐞 **Eşzamanlı ilk puanlama.** Oturum başına tek puan vardır (`session_id` birincil anahtar) ve upsert oku-sonra-yaz'dır. Aynı oturuma eşzamanlı İLK iki puanlamanın ikisi de "kayıt yok" görüp INSERT ediyor, kaybeden birincil anahtar ihlaliyle 500 alıyordu. Kaybeden artık (`23505`) bir kez, kazananın satırını güncelleyerek tekrar dener — son yazan kazanır, tekrar puanlamayla aynı anlam.

> 🐞 **Yorum metni loglanmaz.** Eskiden `Information` seviyesinde yorumun kendisi loglanıyordu; yorum serbest metindir (müşteri telefon/adres yazabilir) ve log kişisel verinin saklanması için tasarlanmış bir yüzey değildir. Log artık yalnızca puanı ve yorum uzunluğunu (`FeedbackLength`) taşır.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Task<ConversationRating> SubmitAsync(string sessionId, int stars, string? feedback)` | Puanı kaydeder (upsert; eşzamanlı ilk kayıtta PK yarışını bir yeniden denemeyle çözer), cache + DB + Redis. |
| `ConversationRating? GetBySession(string sessionId)` | Bir oturumun puanı (varsa). |
| `IReadOnlyList<ConversationRating> GetAll()` | Cache'ten tüm puanlar. |
| `IReadOnlyList<ConversationRating> GetRecent(int count = 20)` | Cache'ten en son N puan. |

## 7. Bağımlılıklar

- `IDbContextFactory<CustomerSupportDbContext>`
- `IMessageBusPort`
- `ILogger<PostgresRatingStore>`

## Bağlantılar

- [IRatingStore](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IRatingStore.md)

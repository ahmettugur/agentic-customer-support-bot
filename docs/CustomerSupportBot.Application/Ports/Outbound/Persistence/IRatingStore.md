# IRatingStore

**Kaynak:** `Ports/Outbound/Persistence/IRatingStore.cs`
**İmplementasyonlar:** [`InMemoryRatingStore`](../../../../CustomerSupportBot.Adapters.Persistence/InMemory/InMemoryRatingStore.md), [`PostgresRatingStore`](../../../../CustomerSupportBot.Adapters.Persistence/Postgres/PostgresRatingStore.md)

## 1. Ne İşe Yarar

Müşteri geri bildirimi (1-5 yıldız + serbest metin yorum) için secondary port.

## 2. Hangi Amaçla Kullanılır

Chat arayüzündeki değerlendirme bileşeni bir oturum kapanırken/bittikten sonra `Submit`
çağırır; admin analytics paneli `GetAll`/`GetRecent` ile ortalama puan/trend gösterir.

## 3. Sorumlulukları

- **Üstlendiği:** Rating kaydı ve sorgulanması.
- **Üstlenmediği:** Puanın konuşma kalitesiyle ilişkilendirilmesi/analiz edilmesi — bu Self-
  Improving Loop'un ([SelfImprovementOptions](../AI/SelfImprovementOptions.md)) işidir; o da bu
  puanı `MinRatingForLesson` eşiğiyle karşılaştırarak ders adayı seçer.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`InMemoryRatingStore` (test) ve `PostgresRatingStore` (prod) implemente eder.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

"Session başına tek rating" kısıtı `Submit`'in XML doc yorumunda açıkça belirtilir — bir
oturum için birden fazla değerlendirme kabul edilmez, bu tekrar oy kullanmayı/puan
manipülasyonunu önler.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `ConversationRating Submit(string sessionId, int stars, string? feedback)` | Yeni değerlendirme kaydeder. |
| `ConversationRating? GetBySession(string sessionId)` | Oturumun rating'i, yoksa `null`. |
| `IReadOnlyList<ConversationRating> GetAll()` | Tüm rating'ler (analytics). |
| `IReadOnlyList<ConversationRating> GetRecent(int count = 20)` | Son N rating. |

## 7. Bağımlılıklar

Port arayüzü `CustomerSupportBot.Domain.Model.ConversationRating`'e bağımlıdır.

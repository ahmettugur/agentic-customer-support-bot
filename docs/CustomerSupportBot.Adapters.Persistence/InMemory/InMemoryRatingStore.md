# InMemoryRatingStore

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/InMemory/InMemoryRatingStore.cs`
- **Port:** `IRatingStore`
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.InMemory`

## 1. Ne İşe Yarar

Konuşma sonu değerlendirme (1-5 yıldız + opsiyonel yorum) kayıtlarını `ConcurrentDictionary<string, ConversationRating>` (session ID → tek değerlendirme) ile bellekte tutar.

## 2. Hangi Amaçla Kullanıldığı

`PostgresRatingStore`'un tek-process karşılığı. Müşterinin sohbet sonunda verdiği puanı saklar/raporlar.

## 3. Sorumlulukları

- `Submit` — puanı `1..5` aralığına `Math.Clamp` ile sıkıştırır, `RatedAt`'i doldurur, aynı session için varsa üzerine yazar (`AddOrUpdate`), bilgi seviyesinde loglar.
- `GetBySession`, `GetAll`, `GetRecent(count)`.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `PostgresRatingStore` (`../Postgres/PostgresRatingStore.md`) ile aynı arayüzü uygular.
- `ConversationRating` modeli Domain katmanındadır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Session başına **tek** değerlendirme varsayımı (`Dictionary` anahtarı session ID) — bir kullanıcının aynı konuşmayı birden fazla puanlaması durumunda son puan geçerli olur (üzerine yazılır), ayrı bir "değerlendirme geçmişi" tutulmaz.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Submit(sessionId, stars, feedback)` | Puanı kaydeder/günceller, `1-5` aralığına kısıtlar. |
| `GetBySession(sessionId)` | Tek kayıt, yoksa `null`. |
| `GetAll()` | Tüm puanlar, en yeniden eskiye. |
| `GetRecent(count)` | Son `count` puan (varsayılan 20). |

## 7. Bağımlılıklar

- `ILogger<InMemoryRatingStore>`

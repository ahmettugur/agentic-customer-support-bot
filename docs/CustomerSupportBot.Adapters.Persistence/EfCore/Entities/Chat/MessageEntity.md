# MessageEntity

**Dosya:** `EfCore/Entities/Chat/MessageEntity.cs`
**Şema/Tablo:** `chat.messages`
**Configuration:** [MessageConfiguration](../../Configurations/Chat/MessageConfiguration.md)

## 1. Ne İşe Yarar

Bir sohbet oturumundaki sıralı user/assistant mesaj geçmişinin tek bir satırını temsil eder.

## 2. Hangi Amaçla Kullanılır

`PostgresSessionManager.GetHistoryAsync`/`AppendAssistantMessage` gibi metotlar bu tabloyu
okur/yazar; LLM'e gönderilecek konuşma geçmişi (`WorkflowMessageBuilder`) buradan inşa edilir.

## 3. Sorumlulukları

- **Üstlendiği:** Tek bir mesajın rolünü (`user`/`assistant`), metnini ve zamanını taşımak.
- **Üstlenmediği:** "Son satırı güncelle mi, yeni satır mı ekle" kararı — bu iş mantığı
  `PostgresSessionManager` içinde uygulanır, entity sadece veri taşıyıcısıdır.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`SessionId` üzerinden [SessionEntity](SessionEntity.md)'ye **gerçek foreign key** ile bağlıdır —
`fk_messages_session`, `Cascade` silme: bir oturum silinirse tüm mesaj geçmişi de silinir.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`(SessionId, Id)` bileşik index'i (`ix_messages_session`), bir oturumun mesajlarını kronolojik
sırada (Id artan olduğu için) verimli çekebilmek içindir — `WHERE session_id = ... ORDER BY id`
sorgu paterni bu index'i tam kullanır.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `Id` | `long` | Birincil anahtar, otomatik artan — aynı zamanda kronolojik sıralama anahtarı. |
| `SessionId` | `string` | FK → `SessionEntity.SessionId`. |
| `Role` | `string` | `"user"` veya `"assistant"`. |
| `Text` | `string` | Mesaj içeriği. |
| `CreatedAt` | `DateTime` | Mesajın oluşturulma zamanı (`timestamptz`). |

## 7. Bağımlılıklar

Yok — saf veri sınıfı.

## Bağlantılar

- [MessageConfiguration](../../Configurations/Chat/MessageConfiguration.md)
- [SessionEntity](SessionEntity.md)
- [README](../README.md)

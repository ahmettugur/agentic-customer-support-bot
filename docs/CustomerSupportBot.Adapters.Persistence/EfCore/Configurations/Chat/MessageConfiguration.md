# MessageConfiguration

**Dosya:** `EfCore/Configurations/Chat/MessageConfiguration.cs`
**Uyguladığı Arayüz:** `IEntityTypeConfiguration<MessageEntity>`
**Entity:** [MessageEntity](../../Entities/Chat/MessageEntity.md)

## 1. Ne İşe Yarar

`MessageEntity`'nin `chat.messages` tablosuna eşlemesini ve `SessionEntity`'ye olan foreign key
ilişkisini tanımlar.

## 2. Hangi Amaçla Kullanılır

`CustomerSupportDbContext.OnModelCreating` tarafından otomatik uygulanır.

## 3. Sorumlulukları

Kolon eşlemeleri; `(SessionId, Id)` bileşik index'i; `SessionEntity`'ye **gerçek** foreign key
(`fk_messages_session`, `Cascade` silme).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`HasOne<SessionEntity>().WithMany()` — tek yönlü bir ilişki tanımı: `SessionEntity` tarafında
karşılık gelen bir `ICollection<MessageEntity>` navigasyonu **yoktur** (entity sınıfı bilinçli
olarak sade tutulmuş, ilişki sadece FK seviyesinde var).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Bu tablodaki `Cascade` silme davranışı, Chat şemasındaki diğer bazı entity'lerin (ör.
`ChatBridgeMessageEntity`) aksine **gerçek bir FK constraint** ile uygulanmıştır — mesaj
geçmişinin, sahibi olan oturumdan asla "yetim" kalmaması (session silinip mesajlar kalması)
istenmiştir; bu bir bütünlük garantisidir.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Configure(EntityTypeBuilder<MessageEntity>)` | `Id` PK (identity); `SessionId`/`Role` zorunlu/≤64,≤16; `Text` zorunlu; `(SessionId, Id)` index; `SessionEntity`'ye FK, `Cascade`. |

## 7. Bağımlılıklar

Yok.

## Bağlantılar

- [MessageEntity](../../Entities/Chat/MessageEntity.md)
- [SessionConfiguration](SessionConfiguration.md)
- [README](../README.md)

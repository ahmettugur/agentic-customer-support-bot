# ChatBridgeMessageConfiguration

**Dosya:** `EfCore/Configurations/Chat/ChatBridgeMessageConfiguration.cs`
**Uyguladığı Arayüz:** `IEntityTypeConfiguration<ChatBridgeMessageEntity>`
**Entity:** [ChatBridgeMessageEntity](../../Entities/Chat/ChatBridgeMessageEntity.md)

## 1. Ne İşe Yarar

`ChatBridgeMessageEntity`'nin `chat.bridge_messages` tablosuna eşlemesini tanımlar.

## 2. Hangi Amaçla Kullanılır

`CustomerSupportDbContext.OnModelCreating` tarafından otomatik uygulanır.

## 3. Sorumlulukları

Kolon eşlemeleri; iki ayrı index — biri oturum bazlı kronolojik okuma için, diğeri
`MessageId` (UI eşleştirme kimliği) ile tekil arama için.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`SessionId` üzerinden [SessionEntity](../../Entities/Chat/SessionEntity.md)'ye mantıksal
referans verir (gerçek FK yok — `MessageConfiguration`'dan farklı olarak burada bilinçli
gevşek bağlama tercih edilmiş).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

İki index'in amacı farklıdır: `ix_bridge_messages_session` (`SessionId, Id`) bir oturumun tüm
köprü mesajlarını kronolojik sırayla çekmek içindir (sayfa açılışında geçmiş yükleme);
`ix_bridge_messages_message_id` ise tek bir mesajı UI'daki kısa `MessageId`'siyle bulmak
içindir (ör. bir mesajın durumunu güncelleme senaryosu).

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Configure(EntityTypeBuilder<ChatBridgeMessageEntity>)` | `Id` PK (identity); `MessageId`/`SessionId`/`Sender` zorunlu; `HumanAgent` opsiyonel; `Text` zorunlu; iki ayrı index. |

## 7. Bağımlılıklar

Yok.

## Bağlantılar

- [ChatBridgeMessageEntity](../../Entities/Chat/ChatBridgeMessageEntity.md)
- [README](../README.md)

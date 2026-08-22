# ChatBridgeMessageEntity

**Dosya:** `EfCore/Entities/Chat/ChatBridgeMessageEntity.cs`
**Şema/Tablo:** `chat.bridge_messages`
**Configuration:** [ChatBridgeMessageConfiguration](../../Configurations/Chat/ChatBridgeMessageConfiguration.md)

## 1. Ne İşe Yarar

"Live Takeover" (bir admin/temsilcinin sohbeti devraldığı) özelliğinin mesaj geçmişini kalıcı
olarak tutan varlıktır — User/Bot/Admin/System gönderen tiplerinin hepsini kapsar.

## 2. Hangi Amaçla Kullanılır

`PostgresChatBridge`/`InMemoryChatBridge` (bkz. Persistence katmanı) canlı sohbet mesajlarını
hem anlık yayınlar (`Channel<T>` pub/sub, DB'ye yazılmadan) hem de bu tabloya kalıcı kayıt
olarak yazar — sayfa yenilendiğinde/yeniden bağlanıldığında geçmiş buradan yüklenir.

## 3. Sorumlulukları

- **Üstlendiği:** Kim gönderdi (`Sender`), hangi insan temsilci (`HumanAgent`, varsa), ne zaman,
  ne metin.
- **Üstlenmediği:** Geçici (`BotTyping` gibi) sinyalleri saklamak — kod yorumunda açıkça
  belirtildiği gibi bunlar DB'ye hiç yazılmaz, sadece in-memory broadcast edilir.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`SessionId` üzerinden [SessionEntity](SessionEntity.md)'ye mantıksal referans verir (gerçek FK
constraint tanımlı değil). `MessageId` alanı, Domain modelindeki 12 karakterlik kısa Id ile UI
tarafındaki mesaj eşleştirmesi için kullanılır (bkz. `ChatBridgeMessage` Domain modeli).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`ChatSender` enum'u (`User`/`Bot`/`Admin`/`System`) veritabanında `string` olarak saklanır
(`Sender` alanı) — enum'un integer karşılığını saklamak yerine string tercih edilmesi, ileride
enum sırası değişse/yeni değer eklense bile eski kayıtların anlamının bozulmamasını sağlar
(integer saklansaydı enum'a yeni bir değer araya eklendiğinde tüm geçmiş veri yanlış yorumlanırdı).

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `Id` | `long` | Birincil anahtar, otomatik artan. |
| `MessageId` | `string` | Domain modelinin 12 karakterlik kısa Id'si, UI eşleştirmesi için. |
| `SessionId` | `string` | İlgili oturum. |
| `Sender` | `string` | `"User"` \| `"Bot"` \| `"Admin"` \| `"System"`. |
| `HumanAgent` | `string?` | Gönderen bir insan temsilciyse adı/kimliği. |
| `Text` | `string` | Mesaj içeriği. |
| `CreatedAt` | `DateTime` | Oluşturulma zamanı (`timestamptz`). |

## 7. Bağımlılıklar

Yok — saf veri sınıfı.

## Bağlantılar

- [ChatBridgeMessageConfiguration](../../Configurations/Chat/ChatBridgeMessageConfiguration.md)
- [SessionEntity](SessionEntity.md)
- [README](../README.md)

# ChatSessionModeEntity

**Dosya:** `EfCore/Entities/Chat/ChatSessionModeEntity.cs`
**Şema/Tablo:** `chat.session_modes`
**Configuration:** [ChatSessionModeConfiguration](../../Configurations/Chat/ChatSessionModeConfiguration.md)

## 1. Ne İşe Yarar

Bir oturumun şu an **Bot** mu yoksa bir insan temsilci tarafından **devralınmış (Human)** mı
olduğunu tutan varlıktır — "Live Takeover" özelliğinin kip (mode) kaydı.

## 2. Hangi Amaçla Kullanılır

`PostgresChatModeRegistry` (`TakeOver`/`Release` metotları) bu tabloyu DB-otoriter kaynak olarak
kullanır — çoklu pod'da hangi pod'un "Human" moda aldığı bilgisinin tüm pod'lara doğru
yansıması için (bkz. Wave C finding-4 ile ilişkili hydration/DB-authority düzeltmesi).

## 3. Sorumlulukları

- **Üstlendiği:** Oturumun güncel kipini (`Mode`), kimin devraldığını (`HumanAgent`), ne zaman
  girildiğini ve o kip içinde kaç mesaj geçtiğini (`MessageCount`) taşımak.
- **Üstlenmediği:** Kip geçiş kurallarının doğrulanması (bir temsilci zaten meşgulse devralamaz
  gibi kurallar) — bu, `ChatPortService`/`PostgresChatModeRegistry` içindeki iş mantığıdır.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`SessionId` (birincil anahtar) [SessionEntity](SessionEntity.md)'nin `SessionId`'siyle mantıksal
olarak eşleşir (gerçek FK yok). `PostgresChatModeRegistry` sınıfı bu entity'yi okuyup Domain'deki
`ChatMode` enum'una çevirir.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 🐞 **Partial index (`HasFilter("mode = 'Human'")`) neden var:** "Şu an devralınmış tüm
> oturumları listele" sorgusu (admin panelindeki aktif takeover listesi) sık çalışır ama
> tablodaki satırların büyük çoğunluğu `Mode = 'Bot'` olacaktır (varsayılan durum). Sadece
> `Mode = 'Human'` olan satırları kapsayan bir **partial index**, tüm tabloyu taramak yerine
> küçük bir alt kümeyi index'ler — hem daha az disk alanı hem daha hızlı sorgu.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `SessionId` | `string` | Birincil anahtar. |
| `Mode` | `string` | `"Bot"` (varsayılan) veya `"Human"`. |
| `HumanAgent` | `string?` | Devralan temsilcinin kimliği (varsa). |
| `EnteredAt` | `DateTime?` | Bu kipe girildiği zaman. |
| `LastActivityAt` | `DateTime?` | Bu kipte son etkinlik zamanı. |
| `MessageCount` | `int` | Bu kipte geçen mesaj sayısı, varsayılan `0`. |

## 7. Bağımlılıklar

Yok — saf veri sınıfı.

## Bağlantılar

- [ChatSessionModeConfiguration](../../Configurations/Chat/ChatSessionModeConfiguration.md)
- [SessionEntity](SessionEntity.md)
- [README](../README.md)

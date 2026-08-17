# ChatSessionState

**Dosya:** `Model/ChatSessionState.cs`  
**Tür:** `class` (mutable)

## 1. Ne İşe Yarar

Bir oturumun **HITL (Human-in-the-Loop) mod durumunun snapshot'ı**dır. Bot mu yoksa insan temsilci mi yanıt veriyor, temsilci adı, son aktivite ve mesaj sayısı bilgilerini tutar.

## 2. Hangi Amaçla Kullanılır

Admin panelindeki "Aktif Sohbetler" listesinde render edilir. Her satırda bir session'ın mevcut modunu (Bot/Human) gösterir. Canlı takeover başladığında veya bittiğinde güncellenir.

> 💡 **Analiz notu:** `SessionState` ile karıştırma! `SessionState` konuşmanın **içeriği** hakkındadır (intent, sentiment, collected info). `ChatSessionState` ise konuşmanın **modu** hakkındadır (bot mu, insan mı yanıtlıyor).

## 3. Sorumlulukları

- ✅ Bir oturumun Bot/Human mod durumunu taşımak
- ✅ Admin UI'da "Aktif Sohbetler" listesini beslemek
- ❌ Oturum içeriğini (intent, sentiment, collected info) taşımak — bu `SessionState`'in işi
- ❌ Mod değiştirme mantığını yürütmek — bu `IChatModeRegistry`'nin işi

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Kim oluşturur:** `IChatModeRegistry` implementasyonları (InMemory veya Postgres)
- **Kim kullanır:** Admin paneli endpoint'leri — `ChatSessionPortService`
- **İlişkili enum:** [ChatMode.md](ChatMode.md)

## 5. Neden Böyle Tasarlandı (Analiz notu)

> 💡 Bu class ayrı bir snapshot çünkü admin paneli sadece mod bilgisini görmek istiyor — tüm `AgentSession` nesnesini yüklemek gereksiz olurdu. Hafif bir DTO olarak tasarlanmış.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `SessionId` | `string` | Oturumun benzersiz kimliği |
| `Mode` | `ChatMode` | Bot veya Human — bkz. [ChatMode.md](ChatMode.md) |
| `HumanAgent` | `string?` | Human modda devralan temsilci adı (Bot modda null) |
| `EnteredAt` | `DateTime?` | Human moda geçiş anı |
| `LastActivityAt` | `DateTime?` | En son mesajın zaman damgası |
| `MessageCount` | `int` | Köprüdeki toplam mesaj sayısı |

## 7. Constructor Bağımlılıkları

Yok — saf veri sınıfı.

## Bağlantılar

- [ChatMode.md](ChatMode.md) — Bot/Human enum tanımı
- [ChatBridgeMessage.md](ChatBridgeMessage.md) — Canlı sohbette akan mesajlar
- [../../CustomerSupportBot.Application/ChatSessionPortService.md](../../CustomerSupportBot.Application/Chat/ChatSessionPortService.md) — Admin panel oturum yönetimi

# ChatBridgeMessage

**Dosya:** `Model/ChatBridgeMessage.cs`  
**Tür:** `class` (mutable)  
**İlişkili:** `ChatBridgeSender` enum (aynı dosyada)

## 1. Ne İşe Yarar

HITL Live Takeover sırasında kullanıcı ↔ admin arasında akan **tek bir mesajın** ortak formatıdır. Hem history buffer'ında saklanır hem SSE event payload'ı olarak iletilir.

## 2. Hangi Amaçla Kullanılır

Bir oturum `ChatMode.Human`'a geçtiğinde, kullanıcının ve admin'in mesajları `IChatBridge` üzerinden `ChatBridgeMessage` formatında yayınlanır. Admin panelinde canlı sohbet akışını ve history'yi render etmek için kullanılır.

> 💡 **Analiz notu:** WhatsApp'taki mesaj baloncuğu gibi düşün — kim gönderdi (User/Bot/Admin/System), ne yazdı, ne zaman yazdı bilgisini taşır.

## 3. Sorumlulukları

- ✅ Tek bir mesajın tüm bilgisini (sender, text, timestamp) taşımak
- ✅ 5 farklı sender tipini desteklemek (User, Bot, Admin, System, BotTyping)
- ❌ Mesajı kalıcı hale getirmek — bu `IChatBridge` implementasyonunun işi
- ❌ Mesajı yayınlamak — bu `IChatBridge.PublishUserMessage/AdminMessage`'ın işi

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Kim oluşturur:** `IChatBridge` implementasyonları (InMemory / Postgres)
- **Kim yayınlar:** `ChatPortService` (kullanıcı mesajı), `ChatSessionPortService` (admin mesajı)
- **Kim tüketir:** Admin paneli SSE aboneliği, chat history API

## 5. Neden Böyle Tasarlandı (Analiz notu)

> 💡 `BotTyping` sender'ı özel — "yazıyor…" göstergesini temsil eder ve **DB'ye yazılmaz**, sadece broadcast edilir. Bu, history'yi gereksiz typing kayıtlarıyla kirletmemek için kasıtlı bir tasarım kararıdır.

## 6. Metotlar / Üyeler

### ChatBridgeMessage

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `Id` | `string` | Benzersiz mesaj ID (12 karakter, GUID'den türetilir) |
| `SessionId` | `string` | Hangi oturuma ait |
| `Sender` | `ChatBridgeSender` | Kim gönderdi (User/Bot/Admin/System/BotTyping) |
| `Text` | `string` | Mesaj içeriği |
| `HumanAgent` | `string?` | Admin mesajlarında temsilci adı |
| `Timestamp` | `DateTime` | Mesajın gönderilme zamanı (UTC) |

### ChatBridgeSender Enum

| Değer | Kaynak | DB'ye Yazılır mı? |
| ------- | -------- | ------------------- |
| `User` | Web client'tan gelen müşteri mesajı | ✅ Evet |
| `Bot` | Agent workflow'unun nihai yanıtı | ✅ Evet |
| `Admin` | İnsan temsilcinin admin panelden yazdığı | ✅ Evet |
| `System` | Bot/Human geçiş bildirimleri ("temsilci katıldı") | ✅ Evet |
| `BotTyping` | Geçici "yazıyor..." göstergesi (on/off) | ❌ **Hayır** |

## 7. Constructor Bağımlılıkları

Yok — saf veri sınıfı.

## Bağlantılar

- [ChatMode.md](ChatMode.md) — Bot/Human mod geçişi
- [ChatSessionState.md](ChatSessionState.md) — Mod snapshot'ı
- [ConversationMessage.md](ConversationMessage.md) — Farkı: bu LLM context için, ChatBridgeMessage canlı sohbet UI için

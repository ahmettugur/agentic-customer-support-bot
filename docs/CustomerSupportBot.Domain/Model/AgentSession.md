# AgentSession

**Dosya:** `Model/AgentSession.cs`  
**Tür:** `class` (mutable)  
**Yaşam döngüsü:** Her oturum için bir örnek — `ISessionManager` tarafından yönetilir

## 1. Ne İşe Yarar

Bir kullanıcının tek bir destek oturumunu temsil eder. Oturum kimliği, oluşturulma zamanı, son aktivite zamanı ve oturumun tüm durumunu taşıyan `SessionState` nesnesini bir arada tutar.

## 2. Hangi Amaçla Kullanılır

Kullanıcı ilk mesajını gönderdiğinde bir `AgentSession` oluşturulur (veya mevcut sessionId ile bulunur). Her API çağrısında `SessionId` ile session bulunur, `LastActivity` güncellenir. Oturum boyunca toplanan bilgiler (müşteri ID, sipariş numarası, niyet, duygu durumu vb.) `State` property'si üzerinden taşınır.

> 💡 **Analiz notu:** Bu class'ı bir "konuşma dosyası" gibi düşünebilirsin. Müşteri destek hattını aradığında temsilcinin önüne açılan dosya gibi — müşteri kim, ne istedi, konuşma hangi aşamada, hangi bilgiler toplandı gibi bilgileri tutar.

## 3. Sorumlulukları

- ✅ Oturum kimliğini ve zaman bilgilerini tutmak
- ✅ Tüm oturum durumunu (`SessionState`) tek bir yapıda birleştirmek
- ❌ Oturumu kaydetmek/yüklemek (bu `ISessionManager`'ın işi)
- ❌ Durumu güncellemek (bu `SessionStateExtractor`'ın işi)

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Kim oluşturur:** `ISessionManager.GetOrCreateAsync()` — Persistence katmanı
- **Kim günceller:** `SessionStateExtractor.ExtractAndApply()` — Domain servisi
- **Kim kullanır:**
  - `ChatPortService` (Application) — her mesaj işleme döngüsünde session'ı alır
  - `ReasoningService` (Application) — reasoning prompt'una state bilgisi enjekte eder
  - `WorkflowRunner` (Adapters.Agents) — ajanların bağlamını kurar

## 5. Neden Böyle Tasarlandı (Analiz notu)

> 💡 `AgentSession` bir **class** (mutable), **record** değil. Çünkü oturum boyunca state sürekli değişir (her turda intent, sentiment, collectedInfo güncellenir). Immutable yapıda bu kadar sık güncelleme performans kaybına ve gereksiz kopyalamaya yol açardı.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `SessionId` | `string` | Oturumun benzersiz kimliği (genellikle GUID) |
| `CreatedAt` | `DateTime` | Oturumun oluşturulma zamanı |
| `LastActivity` | `DateTime` | Son mesaj/aktivite zamanı (timeout kontrolü için) |
| `State` | `SessionState` | Tüm oturum durumu — ayrı doküman: [SessionState.md](SessionState.md) |

## 7. Constructor Bağımlılıkları

Yok — saf veri sınıfı, bağımlılık almaz. Tüm property'ler varsayılan değerle başlatılır.

## Bağlantılar

- [SessionState.md](SessionState.md) — Oturum içindeki türetilmiş durum
- [../Services/SessionStateExtractor.md](../Services/SessionStateExtractor.md) — State'i kim günceller
- [../../CustomerSupportBot.Application/ChatPortService.md](../../CustomerSupportBot.Application/ChatPortService.md) — Bu session'ı kim kullanır

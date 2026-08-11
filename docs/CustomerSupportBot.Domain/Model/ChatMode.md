# ChatMode

**Dosya:** `Model/ChatMode.cs`  
**Tür:** `enum`

## 1. Ne İşe Yarar

Bir oturumun mesaj işleme modunu belirler: **Bot** (ajan workflow çalışır) veya **Human** (insan temsilci doğrudan yanıt verir, bot devre dışıdır).

## 2. Hangi Amaçla Kullanılır

HITL (Human-in-the-Loop) Live Takeover özelliğinde kullanılır. Admin panelinden bir temsilci "Devrala" butonuna bastığında session modu `Human`'a geçer; bıraktığında `Bot`'a döner.

> 💡 **Analiz notu:** Gerçek hayattaki müşteri destek hatlarında olduğu gibi — bazen bot yetmez, insan temsilci devralır. Bu enum o geçişi temsil eder.

## 3. Sorumlulukları

- ✅ İki durumdan birini temsil etmek (Bot / Human)
- ❌ Mod geçiş mantığını yürütmek — bu `IChatModeRegistry`'nin işi

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Kullanan tipler:** `ChatSessionState.Mode`, `IChatModeRegistry`
- **Mod kontrol eden:** `ChatPortService.HandleStreamAsync()` — her mesajda mod kontrolü yapar
- **Mod değiştiren:** `HumanAgentPortService` — TakeOver/Release endpoint'leri

## 5. Neden Böyle Tasarlandı (Analiz notu)

> 💡 Sadece iki durum var (Bot/Human) çünkü sistem iki mutually exclusive modda çalışır. "Hybrid" mod kasıtlı olarak yok — ya bot yanıtlıyor ya da insan. Bu basitlik, state machine'i güvenilir ve debug edilebilir tutar.

## 6. Enum Değerleri

| Değer | Açıklama |
|-------|----------|
| `Bot` | Normal ajan workflow'u çalışır — LLM reasoning + agent team |
| `Human` | Canlı takeover — bir insan agent doğrudan kullanıcıyla konuşuyor; bot devre dışı |

`IChatModeRegistry` her session için modu tutar; mode değişiklikleri Redis pub/sub ile broadcast edilir (multi-pod sync).

## 7. Constructor Bağımlılıkları

Yok — enum tipi.

## Bağlantılar

- [ChatSessionState.md](ChatSessionState.md) — Bu enum'u kullanan snapshot model
- [ChatBridgeMessage.md](ChatBridgeMessage.md) — Canlı sohbet mesajları
- [../../CustomerSupportBot.Application/ChatPortService.md](../../CustomerSupportBot.Application/ChatPortService.md) — Mod kontrolünün yapıldığı yer

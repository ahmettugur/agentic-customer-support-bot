# RealtimeBridgeService

**Dosya:** `Services/Realtime/RealtimeBridgeService.cs`

## 1. Ne İşe Yarar

HITL canlı sohbet köprüsü — bot modundan insan temsilci moduna geçişi, mesaj yönlendirmesini ve mod değişikliğini yönetir.

## 2. Hangi Amaçla Kullanılır

Eskalasyon sonrası temsilci chat'e bağlanırken `ChatMode.Human` moduna geçiş sağlar. Temsilci mesajları `ChatBridgeMessage` formatında taşınır.

> 💡 **Analiz notu:** Bir çağrı merkezinin "şimdi sizi yetkili temsilcimize bağlıyorum" anonsundan sonra hat değişimi gibi — bot'tan insana geçiş.

## Bağlantılar

- [../../CustomerSupportBot.Domain/Model/ChatMode.md](../../CustomerSupportBot.Domain/Model/ChatMode.md) — Bot/Human modları
- [../../CustomerSupportBot.Domain/Model/ChatBridgeMessage.md](../../CustomerSupportBot.Domain/Model/ChatBridgeMessage.md) — Mesaj formatı

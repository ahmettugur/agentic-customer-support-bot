# ChatEventOrchestrator

- **Kaynak:** `CustomerSupportBot.Api/Services/ChatEventOrchestrator.cs`
- **Tür:** `public sealed class`
- **Namespace:** `CustomerSupportBot.Api.Services`

## Ne işe yarar?

`ChatEventOrchestrator`, çok kanallı (Multi-Channel) sohbet sisteminde bot akışı, insan müşteri temsilcisi (`HumanAgent`) modu ve hibrit modlar arasındaki geçişleri koordine eden; gelen müşteri mesajlarını doğru kanala (Bot MAF Workflow veya İnsan Temsilci Köprüsü) yönlendiren orkestrasyon servisidir.

## Hangi amaçla kullanılır`?

- Oturumun [IChatSessionModeRegistry](../../CustomerSupportBot.Application/Ports/Outbound/Chat/IChatModeRegistry.md) üzerindeki aktif modunu (`Bot`, `Human`, `Hybrid`) kontrol etmek.
- Mod `Human` ise mesajı doğrudan [IChatBridge](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IChatBridge.md) üzerinden temsilcinin paneline iletmek ve temsilci yanıtını beklemek.
- Mod `Bot` ise standart [IChatOrchestratorPort](../../CustomerSupportBot.Application/Ports/Inbound/IChatOrchestratorPort.md) hattını işletmek.

## Sorumlulukları

- **Üstlendiği:**
  - `ProcessTurnAsync` ve `ProcessStreamingTurnAsync` metotlarıyla çok modlu sohbet turlarını yürütmek.
  - Mod geçişlerinde (`HandoffToHuman`, `ReturnToBot`) bildirim olayları üretmek.

## Bağımlılıklar

- [IChatOrchestratorPort](../../CustomerSupportBot.Application/Ports/Inbound/IChatOrchestratorPort.md)
- [IChatBridge](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IChatBridge.md)
- [IChatSessionModeRegistry](../../CustomerSupportBot.Application/Ports/Outbound/Chat/IChatModeRegistry.md)

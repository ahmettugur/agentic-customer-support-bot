# CustomerSupportBot.Adapters.AI.Realtime

Bu klasör, OpenAI Realtime API (WebSocket / WebRTC) üzerinden gerçek zamanlı çift yönlü ses iletişimi ve sesli oturum araçlarını yöneten adaptörleri barındırır.

## Dosyalar

- [OpenAiRealtimeClientAdapter](OpenAiRealtimeClientAdapter.md) — [IRealtimeVoiceTransport](../../CustomerSupportBot.Application/Ports/Outbound/AI/IRealtimeVoiceTransport.md) portunu uygulayan; WebSocket bağlantısı, ses tamponlama, session.update yapılandırması ve sesli araç yanıtlarını yöneten ana adaptör.
- [RealtimeFunctionTools](RealtimeFunctionTools.md) — Realtime ses oturumu için OpenAI function calling formatında JSON Schema tanımlarını sunan salt-okunur araç kataloğu.

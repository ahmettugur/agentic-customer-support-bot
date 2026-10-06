# CustomerSupportBot.Application.Services.Voice

Temsilci ile müşteri arasında canlı sesli görüşme (WebRTC, eşler arası). Tasarım:
[docs/superpowers/specs/2026-10-06-agent-voice-call-design.md](../../../superpowers/specs/2026-10-06-agent-voice-call-design.md).

## Akış

1. Temsilci devraldığı sohbette arar → [VoiceCallService](VoiceCallService.md) `StartAsync`; müşteriye
   `voice_signal` (`ring`) gider.
2. Müşteri rıza metnini görüp kabul eder; offer/answer/ice sinyalleri sunucu üzerinden (`IChatBridge`, SSE)
   aktarılır, ses doğrudan iki tarayıcı arasında akar (gerekirse coturn TURN).
3. Temsilcinin tarayıcısı iki izi (kendi mikrofonu, müşterinin sesi) ayrı ayrı kaydeder ve 10 sn'lik
   parçalar halinde yükler.
4. [VoiceTranscriptionProcessor](VoiceTranscriptionProcessor.md) parçaları sırayla yazıya döker; satırlar
   görüşme sürerken temsilci panelinde görünür.
5. Görüşme bitince iki tarafın geçmişine "Sesli görüşme · X dk Y sn" notu düşer.

## Dosyalar

- [VoiceCallService](VoiceCallService.md) — `IVoiceCallPort` uygulaması (başlat, kabul, ret, kapat, sinyal, parça yükleme, zaman aşımı).
- `TurnCredentialFactory` — coturn REST kimlik bilgisi (`kullanıcı = son-geçerlilik:callId`, parola = HMAC-SHA1(`SharedSecret`)).
- [VoiceTranscriptionProcessor](VoiceTranscriptionProcessor.md) — döküm kuyruğunu işler.

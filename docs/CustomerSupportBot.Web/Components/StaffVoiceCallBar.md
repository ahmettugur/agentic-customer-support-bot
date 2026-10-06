# StaffVoiceCallBar

- **Kaynak:** `CustomerSupportBot.Web/Components/StaffVoiceCallBar.razor`
- **Kullanım:** Admin "Canlı sohbetler" panelinin başlığı (`SessionId` parametresi, `@key` ile oturum başına yeniden kurulur)

## Ne işe yarar?

Temsilcinin sesli görüşme denetimi: **Sesli görüşme** düğmesi; görüşmede durum (Aranıyor… / Bağlanıyor… /
Görüşmede), süre, **Kayıt** rozeti, sessize al, İptal/Bitir.

- Açılışta `GET /voice-calls/mine`: temsilci başka sohbette görüşmedeyse düğme pasif, ipucu
  "Zaten bir sesli görüşmedesiniz.".
- WebRTC ve kayıt `wwwroot/js/agent-voice-call.js` (`csbVoice.staffStart`) tarafından yürütülür; durum
  `OnVoiceState` / `OnVoiceRecording` / `OnVoiceError` ile bileşene döner.
- `OnSignal(callId, type, reason)`: Admin sayfası SSE'deki `voice_signal`'i iletir. Ret ve cevapsız
  bildirimi son başlatılan görüşmenin kimliğiyle eşleşir; JS aynı sinyalde çubuğu önce temizlese de
  bildirim kaybolmaz.
- Bileşen kapanırken açık görüşme kapatılır.

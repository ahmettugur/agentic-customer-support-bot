# StaffVoiceCallBar

- **Kaynak:** `CustomerSupportBot.Web/Components/StaffVoiceCallBar.razor`
- **Kullanım:** Admin "Canlı sohbetler" panelinin başlığı (`SessionId` parametresi, `@key` ile oturum başına yeniden kurulur)

## Ne işe yarar?

Temsilcinin sesli görüşme denetimi: **Sesli görüşme** düğmesi; görüşmede durum (Aranıyor… / Bağlanıyor… /
Görüşmede), süre, **Kayıt** rozeti, sessize al, İptal/Bitir.

- Açılışta `GET /voice-calls/mine`: temsilci başka sohbette görüşmedeyse düğme pasif, ipucu
  "Zaten bir sesli görüşmedesiniz.".
- WebRTC, kayıt ve sinyal dinleme `wwwroot/js/agent-voice-call.js` (`csbVoice`) modülündedir; modül
  görüşmenin oturumuna kendi SSE akışını açar (`/agent/chat-sessions/{sid}/subscribe`). Görüşme bu yüzden
  çubuğa bağlı değildir: temsilci başka sohbete ya da sekmeye geçince sürer.
- Çubuk açılınca, bu sohbette süren görüşme varsa `csbVoice.attach` ile ona bağlanır (durum, süre, kayıt,
  sessiz); kapanınca `csbVoice.detach` yalnızca bildirimleri durdurur, görüşmeyi kapatmaz.
- JS geri çağrıları: `OnVoiceState` (aranıyor / bağlanıyor / görüşmede), `OnVoiceRecording`, `OnVoiceError`,
  `OnVoiceEnded(type, reason)` — ret ve cevapsız için bildirim (`VoiceCallText.EndNotice`).

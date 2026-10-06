# VoiceCallPlayer

- **Kaynak:** `CustomerSupportBot.Web/Components/VoiceCallPlayer.razor`
- **Kullanım:** Admin sohbet geçmişinde döküm satırındaki zamana tıklanınca açılan modal (`CallId`, `StartOffsetMs`)

## Ne işe yarar?

Görüşme kaydını dinletir: `GET /voice-calls/{id}` ile satırları yükler, **Baştan dinle** ya da bir
satırın zamanıyla o noktadan çalar. İki iz (temsilci, müşteri) `csbVoicePlayer.play` ile aynı anda,
parça parça sırayla çalınır; parça sesi `GET /voice-calls/{id}/chunks/{chunkId}` (yetkili istek) ile alınır.
Kapanınca oynatma durur.

Ses 90 gün sonra silindiğinde satırlar kalır, parçalar çalınamaz.

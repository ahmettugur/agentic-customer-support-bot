# VoiceTranscriptionProcessor

- **Kaynak:** `CustomerSupportBot.Application/Services/Voice/VoiceTranscriptionProcessor.cs`
- **Tür:** `public sealed class` (singleton; [VoiceCallWorker](../../../CustomerSupportBot.Api/Workers/VoiceCallWorker.md) çağırır)

## Ne işe yarar?

`ProcessNextAsync` sıradaki bekleyen parçayı sahiplenir, [IAudioTranscriber](../../../CustomerSupportBot.Adapters.AI/Audio/OpenAiAudioTranscriber.md)
ile yazıya döker ve sonucu yayınlar. İş yoksa `false` döner.

1. **Sahiplenme** depoda yapılır (`TryClaimNextPendingAsync`, koşullu UPDATE); çok pod'da aynı parça iki
   kez işlenmez. 5 dk'dan uzun `Processing`'de kalan parça yeniden sahiplenilebilir.
2. **Bütçe:** `ILlmSpendGuard` oturum bütçesini aşmış diyorsa parça 1 dk ertelenir.
3. **Hata:** deneme sayısı artar, üstel bekleme (10 sn, 20 sn, …). `TranscriptionMaxAttempts` dolunca parça
   `Failed` olur ve panele "(döküm alınamadı)" satırı düşer.
4. **Başarı:** satır "Müşteri: …" / "Temsilci (ad): …" olarak yalnızca temsilci tarafına yayınlanır
   (`PublishVoiceTranscriptAsync`, `voiceCallId` / `voiceTrack` / `offsetMs` ile). Metin ayrıca konuşma
   geçmişine eklenir (müşteri → kullanıcı mesajı, temsilci → asistan mesajı); duygu analizi ve bota
   dönüşte bağlam bunu kullanır.

Boş döküm (sessizlik) satır üretmez.

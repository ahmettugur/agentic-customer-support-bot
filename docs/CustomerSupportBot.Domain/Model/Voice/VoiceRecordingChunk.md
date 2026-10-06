# VoiceRecordingChunk

- **Kaynak:** `CustomerSupportBot.Domain/Model/Voice/VoiceRecordingChunk.cs`
- **Tür:** `public sealed class` (+ `VoiceTrack { Agent, Customer }`, `VoiceTranscriptStatus { Pending, Processing, Done, Failed }`)
- **Tablo:** `voice.recording_chunks`

## Ne işe yarar?

Görüşme kaydının bir parçası: tek iz (temsilci ya da müşteri), ~10 sn, kendi başına çalınabilen tam bir
`audio/webm` dosyası. `OffsetMs` görüşme başından itibaren konumdur; oynatıcı iki izi bununla hizalar.

Döküm durumu parça üzerinde tutulur (`TranscriptStatus`, `TranscriptText`, `Attempts`, `NextAttemptAt`,
`ClaimedAt`); kuyruk ayrı bir tablo değildir. `(call_id, track, sequence)` tekildir, yani aynı parçanın
yeniden yüklenmesi ikinci kayıt oluşturmaz.

Saklama: ses verisi 90 gün sonra silinir (`AudioPurgedAt`), döküm metni kalır.

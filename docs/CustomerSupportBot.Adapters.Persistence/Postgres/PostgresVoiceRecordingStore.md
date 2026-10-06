# PostgresVoiceRecordingStore

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/Postgres/PostgresVoiceRecordingStore.cs`
- **Tür:** `public sealed class : IVoiceRecordingStore`
- **Tablo:** `voice.recording_chunks` (migration `AddVoiceCalls`)

## Ne işe yarar?

Kayıt parçalarını (`bytea`) ve döküm kuyruğu durumunu saklar. Cache tutmaz.

| Üye | Açıklama |
|---|---|
| `TryAddAsync` | `(call_id, track, sequence)` tekil; tekrar yüklenen parça yok sayılır. |
| `TryClaimNextPendingAsync` | En eski 5 aday; her biri için `status`+`claimed_at` koşullu UPDATE — başka pod kaptıysa sıradaki denenir. 5 dk'dan eski `Processing` yeniden alınabilir. |
| `CompleteAsync` / `FailAttemptAsync` / `PostponeAsync` | Döküm sonucu, başarısız deneme (son denemede `Failed`), bütçe ertelemesi. |
| `ListMetaAsync` | Görüşmenin satırları — ses verisi **okunmaz**. |
| `GetAsync` | Ses verisiyle tek parça (oynatıcı). |
| `PurgeAudioCreatedBeforeAsync` | Saklama süresi (90 gün): ses boşaltılır, `audio_purged_at` yazılır, döküm kalır. |

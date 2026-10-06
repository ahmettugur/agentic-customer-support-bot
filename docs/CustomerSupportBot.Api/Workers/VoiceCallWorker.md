# VoiceCallWorker

- **Kaynak:** `CustomerSupportBot.Api/Workers/VoiceCallWorker.cs`
- **Tür:** `BackgroundService`

İki iş yapar:

- **Döküm:** [VoiceTranscriptionProcessor](../../CustomerSupportBot.Application/Services/Voice/VoiceTranscriptionProcessor.md)
  `ProcessNextAsync`'i iş oldukça beklemeden çağırır; iş yoksa 2 sn uyur.
- **Süpürme:** 5 sn'de bir `IVoiceCallPort.SweepAsync` — cevaplanmayan çalma `Missed`, parçası kesilen
  aktif görüşme `Failed`.

**Çok pod:** kilit gerekmez. Parça sahiplenmesi ve durum geçişleri koşullu UPDATE olduğundan her pod
çalışabilir; aynı iş iki kez yapılmaz.

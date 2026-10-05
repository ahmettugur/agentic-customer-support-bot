# DataRetentionService

- **Kaynak:** `CustomerSupportBot.Api/Workers/DataRetentionService.cs`
- **Tür:** `BackgroundService`

`DataRetention:SweepIntervalMinutes`'te bir `IDataPrivacyPort.RunRetentionAsync`'i çağırır. Ayar çalışırken
değişebilir (`IOptionsMonitor`): kapalıyken döngü uyur, açılınca tarar.

**Çok pod:** dağıtık kilit (`privacy:retention:sweep`) **beklenmeden** denenir; başka bir pod taramadaysa bu
pod o turu atlar. Silme tekrarlanabilir olduğundan iki pod'un art arda taraması zararsızdır. Redis yoksa her
pod kendi taramasını yapar.

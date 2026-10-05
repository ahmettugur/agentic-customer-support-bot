# CustomerSupportBot.Application.Services.Privacy

Kişisel veri (KVKK): otomatik saklama süresi temizliği, müşterinin verisini dışa aktarma ve silme.
Tasarım: [docs/superpowers/specs/2026-10-05-data-retention-design.md](../../../superpowers/specs/2026-10-05-data-retention-design.md).

## Dosyalar

- [DataPrivacyService](DataPrivacyService.md) — `IDataPrivacyPort` uygulaması.
- `EpisodicMemoryEraser` — episodik bellekteki (Qdrant) oturum ve müşteri etiketli kayıtları siler;
  yalnızca bellek açıkken kayıtlıdır. Vektör deposu silinen sayıyı döndürmediği için raporda işlenen
  oturum sayısı görünür.

## Silmeye katılan depolar (`ISessionDataEraser`)

| Depo | Ne silinir | Not |
|---|---|---|
| `attachments` | Oturumun fotoğrafları | Oturum silinince FK cascade da siler; burada açıkça silinir. |
| `bridge-messages` | Canlı devralma mesajları | Açık canlı abonelikler kapatılmaz. |
| `chat-modes` | Oturumun bot/temsilci modu | |
| `ratings` | Puan ve yorum | Yorum serbest metindir. |
| `escalations` | Kapanmış eskalasyonlar silinir | **Açık** olanlar silinmez (kuyruk ve yük sayaçları); müşteri metni temizlenir. |
| `reasoning-traces` | Akıl yürütme izleri | Soru/yanıt metni içerir. |
| `conversation-dispositions` | Temsilcinin kapanış kayıtları (neden, etiket, not) | Not serbest metindir. Dışa aktarmada oturumun altında yer alır (kapatan temsilcinin adı hariç). |
| `episodic-memory` | Qdrant episodik kayıtları | Bellek kapalıyken kayıtlı değil. |

Önbellekli depolar silmeden sonra `csbot:privacy:sessions-erased` kanalına yayın yapar; her depo bu
kanala abonedir, diğer pod'lar da önbelleğinden çıkarır. Yeni bir depo silmeye katılmak için
`ISessionDataEraser`'ı uygular ve DI'a kaydedilir (`PersistenceAdapterServiceCollectionExtensions.AddSessionDataStore`).

## Yapılandırma (`DataRetention`)

| Ayar | Varsayılan | Anlamı |
|---|---|---|
| `Enabled` | `true` | Otomatik temizlik. Kapalıyken dışa aktarma/silme yine çalışır. |
| `ConversationRetentionDays` | `180` | Son etkinliği bundan eski oturumlar silinir. `0` = sohbet temizliği kapalı. |
| `AttachmentRetentionDays` | `90` | Bundan eski fotoğraflar silinir (oturum daha yeni olsa da). `0` = kapalı. |
| `SweepIntervalMinutes` | `60` | Tarama aralığı (`DataRetentionService`). |
| `MaxSessionsPerSweep` | `500` | Bir taramada en fazla oturum; kalanlar sonraki taramaya. |

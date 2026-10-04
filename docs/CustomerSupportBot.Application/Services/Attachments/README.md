# CustomerSupportBot.Application.Services.Attachments

Müşterinin yazılı sohbette fotoğraf eklemesi (hasarlı ürün, iade edilecek ürün, sipariş edilmek
istenen ürün). Tasarım: [docs/superpowers/specs/2026-10-04-photo-attachments-design.md](../../../superpowers/specs/2026-10-04-photo-attachments-design.md).

## Akış

1. `POST /chat/attachments` → [ChatAttachmentService](ChatAttachmentService.md): boyut ve dosya imzası
   kontrolü, [ImageSanitizer](ImageSanitizer.md) ile meta veri silme, görsel modelle kısa açıklama
   (`IImageAnalysisPort`), açıklamanın `IInputGuard`'dan geçmesi, `IAttachmentStore`'a kayıt.
   Oturum yoksa burada açılır ve müşteriye bağlanır.
2. Mesaj `attachmentIds` ile gönderilir → `ChatPortService` [AttachmentTurnContext](AttachmentTurnContext.md)
   ile yalnızca bu oturuma ve bu müşteriye ait fotoğrafları kabul eder, açıklamalarını kullanıcı
   mesajına ekler ve fotoğrafları "gönderildi" (`SentAt`) olarak işaretler.
3. Yan etkili bir tool onaya gittiğinde [SideEffectApprovalGate](../Approval/SideEffectApprovalGate.md)
   oturumun **gönderilmiş** ve henüz bağlanmamış fotoğraflarını `Parameters["attachmentIds"]`'e ekler
   ve onları o onay kaydına bağlar.
4. Admin/temsilci onay kartında fotoğrafları görür (`GET /attachments/{id}`, `GET /agent/attachments/{id}`).

## Dosyalar

- [ChatAttachmentService](ChatAttachmentService.md) — `IChatAttachmentPort` uygulaması (yükle, getir, gönderilmemişi sil).
- [ImageSanitizer](ImageSanitizer.md) — saf: tür tespiti (dosya imzası) ve meta veri silme.
- [AttachmentTurnContext](AttachmentTurnContext.md) — fotoğrafları doğrulayıp açıklamalarını tur metnine ekler.

## Yapılandırma (`Attachments`)

| Ayar | Varsayılan | Anlamı |
|---|---|---|
| `Enabled` | `true` | Kapalıysa yükleme `attachments_disabled` ile reddedilir. |
| `MaxBytes` | `5242880` | Dosya başına üst sınır (5 MB). Uçta ayrıca 12 MB gövde sınırı var. |
| `MaxPerMessage` | `3` | Bir mesajda kabul edilen fotoğraf sayısı (fazlası yok sayılır). |
| `MaxPerSession` | `10` | Oturum başına toplam fotoğraf. |

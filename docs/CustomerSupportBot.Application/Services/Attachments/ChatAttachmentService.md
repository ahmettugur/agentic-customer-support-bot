# ChatAttachmentService

- **Kaynak:** `CustomerSupportBot.Application/Services/Attachments/ChatAttachmentService.cs`
- **Tür:** `public sealed class : IChatAttachmentPort`

## Ne işe yarar?

Müşterinin yüklediği fotoğrafı doğrular, meta verisini siler, görsel modelle kısa bir açıklama
üretir ve kaydeder. Müşterinin kendi fotoğrafını getirmesini ve henüz göndermediği fotoğrafı
silmesini de sağlar.

## Yükleme sırası (`UploadAsync`)

1. `Enabled` → `Disabled`; boş veri → `Empty`; `MaxBytes` üstü → `TooLarge`.
2. Tür **dosya imzasından** belirlenir; JPEG/PNG değilse ya da bozuksa → `UnsupportedType`.
3. Meta veri silinir ([ImageSanitizer](ImageSanitizer.md)) — saklamadan ve görsel modele
   göndermeden önce.
4. Oturum: `sessionId` biçimi geçersizse → `InvalidSession`. Oturum yoksa açılır ve müşteriye
   bağlanır; başka müşteriye aitse → `Forbidden`. Bağlama, sohbet turuyla aynı kilit anahtarı
   (`SessionIdentityBinder.TurnLockKey`) altında yapılır; oturum zaten bu müşteriye bağlıysa kilit alınmaz.
5. Oturumdaki fotoğraf sayısı `MaxPerSession`'a ulaştıysa → `TooManyInSession`.
6. Görsel model açıklaması: hata yüklemeyi bozmaz (açıklama `null`); metin `IInputGuard`'dan geçer —
   kişisel veri maskelenir, enjeksiyon olarak reddedilirse açıklama kullanılmaz.
7. Kayıt; yanıt `{ attachmentId, sessionId, description }`.

## Diğer üyeler

| Üye | Açıklama |
|---|---|
| `GetAsync(id, customerId)` | `customerId` verilirse yalnızca o müşterinin fotoğrafı (müşteri ucu); `null` ise sahiplik kontrolü yok (personel uçları — yetki grup düzeyinde). |
| `DeleteUnsentAsync(id, customerId)` | Müşterinin önizlemeden kaldırdığı fotoğraf. Gönderilmiş fotoğraf silinmez (geçmişte ve olası bir onay kaydında yer alır). |

## Bağımlılıklar

`IAttachmentStore`, `ISessionManager`, `IAppDistributedLock`, `IImageAnalysisPort`, `IInputGuard`,
`IOptions<AttachmentOptions>`, `ILogger`.

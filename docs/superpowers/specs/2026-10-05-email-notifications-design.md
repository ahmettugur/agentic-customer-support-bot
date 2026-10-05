# E-posta Bildirimi — Tasarım

**Tarih:** 2026-10-05 · **Dal:** `feature/email-notifications`

## Amaç

Onaylar bloklamıyor: sipariş, iptal, iade ve şikayet talebinin sonucu müşteriye yalnızca uygulama içi
bildirim olarak gidiyor; sayfayı kapatan müşteri sonucu ancak geri gelince görüyor. Sonuç artık e-postayla
da gönderilir. Gönderim tamamen `appsettings`'ten ayarlanır (kullanıcı isteği).

Başarı ölçütleri:
- Onay sonuçlandığında (onaylandı ve yürütüldü / yürütme başarısız / reddedildi / zaman aşımı) müşterinin
  kayıtlı e-posta adresine bir e-posta gider.
- Çok pod'lu kurulumda e-posta **bir kez** gider.
- SMTP sunucusu, port, şifreleme, kullanıcı, parola, gönderen ve açık/kapalı `appsettings`'ten gelir;
  kapalıyken hiçbir şey gönderilmez.

## Yapılandırma (`Email`)

| Ayar | Varsayılan | Anlamı |
|---|---|---|
| `Enabled` | `false` | Tüm e-posta gönderimi. |
| `FromAddress` / `FromName` | — / `Müşteri Destek` | Gönderen. `Enabled` iken `FromAddress` zorunlu. |
| `PublicBaseUrl` | boş | E-postadaki "uygulamada görüntüle" bağlantısı; boşsa bağlantı konmaz. |
| `Smtp:Host` / `Smtp:Port` | — / `587` | `Enabled` iken `Host` zorunlu. |
| `Smtp:Security` | `StartTls` | `None`, `StartTls`, `SslOnConnect`, `Auto`. |
| `Smtp:Username` / `Smtp:Password` | boş | Boşsa kimlik doğrulama yapılmaz. Parola ortam değişkeni/secret ile verilmeli. |
| `Smtp:TimeoutSeconds` | `30` | |
| `Notifications:ApprovalResults` | `true` | Onay sonucu e-postası. |

`Enabled=true` iken eksik/geçersiz ayar uygulama başlangıcında hata verir (sessizce gönderilmeyen e-posta
yerine).

## Mimari

- **Port `IEmailSender`** (outbound): `SendAsync(EmailMessage)`. Adaptör: yeni `CustomerSupportBot.Adapters.Email`
  projesi, MailKit ile SMTP. Kapalıyken `NullEmailSender` (gönderim yok).
- **Port `INotificationLedger`** (outbound): `TryClaimAsync(key)` / `ReleaseAsync(key)`. Postgres'te
  `notifications.sent_log(key PK)`; `INSERT … ON CONFLICT DO NOTHING` — yalnızca bir pod "talep eder".
  Sonraki bildirim türleri (proaktif kargo bildirimi) aynı defteri kullanır.
- **`ApprovalResultEmailService`** (Application): `IApprovalQueue.RequestDecided` olayını işler. Bu olay
  karar veren pod'da **ve** pub/sub ile diğer pod'larda tetiklenir; bu yüzden gönderimden önce
  `approval-result:{id}` anahtarı talep edilir. Bekleyen kayıt, müşterisiz kayıt, kapalı ayar → atlanır.
  Gönderim başarısız olursa talep geri bırakılır ve hata loglanır (uygulama içi bildirim yine vardır).
- **Müşteri e-postası:** `ICustomerRepository.GetEmailAsync` (katalog müşteri kaydı). Adres yoksa atlanır.
  Loglarda adres maskelenir (`PiiMasker.MaskEmail`).
- **Barındırma:** `ApprovalEmailNotificationService` (Api, `IHostedService`) olaya başlangıçta abone olur.
- **İçerik:** Türkçe, düz metin + HTML (kullanıcı metinleri HTML-escape). Konu: "Talebiniz onaylandı — İade"
  gibi; gövdede işlem, sonuç metni (yürütme sonucu ya da red gerekçesi) ve varsa bağlantı.

## Hata yönetimi

- SMTP hatası: loglanır, defter kaydı geri bırakılır. Otomatik yeniden deneme yok (kapsam dışı).
- Olay işleyicisi hiçbir zaman karar akışını bozmaz (fire-and-forget + catch).

## Test

- `ApprovalResultEmailService`: iki "pod" aynı olayı işlediğinde tek e-posta; kapalı ayar, müşterisiz,
  e-postasız, bekleyen kayıt atlanır; içerik; gönderim hatasında talebin geri bırakılması.
- Postgres defter (Testcontainers): tek talep, geri bırakma sonrası yeniden talep.
- SMTP adaptörü: gerçek bir SMTP test sunucusuna (Mailpit, Testcontainers) gönderim, konu/alıcı/gövde.
- Ayar doğrulaması: `Enabled` iken eksik `Host`/`FromAddress` başlangıçta hata.
